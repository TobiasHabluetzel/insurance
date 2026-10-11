using System.Collections.Concurrent;
using System.Text.Json;

namespace Insurance.Api.Services;

/// <summary>
/// Keeps the behavioural signal batches per claim so something can read them
/// back.
///
/// Until now the collector's output was POSTed to /behavior, emailed to an
/// analyst, and then dropped — there was no way to see it from the application.
/// The management view needs exactly that data, so the batches are retained
/// here as well as emailed.
///
/// In memory, deliberately, mirroring <see cref="ClaimEventService"/>: this is a
/// demo stack with no claims table, and a restart losing behavioural telemetry
/// is the same trade already accepted for the live claim event stream. Move it
/// to the claims projection when one exists.
/// </summary>
public class BehaviorLogService
{
    /// <summary>One POST from the collector — a stage's worth of events.</summary>
    /// <param name="Stage">"submit" or "evidence" — which point in the flow.</param>
    /// <param name="ReceivedAt">Server receipt time; the events carry their own.</param>
    /// <param name="PriorSubmittedAt">
    /// Epoch ms of an earlier registration from the same browser, when there was
    /// one. This is what makes "second claim from this device" visible.
    /// </param>
    public record Batch(string Stage, DateTimeOffset ReceivedAt, long? PriorSubmittedAt, JsonElement Events);

    private readonly ConcurrentDictionary<string, List<Batch>> _byClaim = new();

    /// <summary>
    /// Store a batch. The collector sends its events as a JSON string, so it is
    /// parsed once here rather than making every reader deal with it — and a
    /// malformed payload is dropped rather than poisoning the log, because
    /// telemetry must never be the thing that breaks a claim.
    /// </summary>
    public void Add(string claimId, string stage, string? eventsJson, long? priorSubmittedAt)
    {
        if (string.IsNullOrWhiteSpace(claimId)) return;

        JsonElement parsed;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(eventsJson) ? "[]" : eventsJson);
            parsed = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        var batch = new Batch(stage, DateTimeOffset.UtcNow, priorSubmittedAt, parsed);
        _byClaim.AddOrUpdate(
            claimId,
            _ => [batch],
            (_, existing) =>
            {
                // The collector posts once per stage, so contention is unlikely —
                // but AddOrUpdate's factory can run more than once, so the list
                // still needs its own lock.
                lock (existing) existing.Add(batch);
                return existing;
            });
    }

    /// <summary>Everything recorded for a claim, oldest batch first.</summary>
    public IReadOnlyList<Batch> Get(string claimId)
    {
        if (!_byClaim.TryGetValue(claimId, out var batches)) return [];
        lock (batches) return batches.OrderBy(b => b.ReceivedAt).ToArray();
    }
}
