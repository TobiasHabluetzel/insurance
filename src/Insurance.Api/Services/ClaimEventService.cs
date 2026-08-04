using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Insurance.Api.Services;

public class ClaimEventService
{
    private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();
    // Last published event per claim, so a subscriber that connects after the
    // event was already published (e.g. a user clicking an email link long
    // after AC's webhook fired) still gets the current state on connect.
    private readonly ConcurrentDictionary<string, string> _latestEvent = new();
    // Tracks which claims we've already sent the "missing evidence" email for,
    // so a re-fire of claim.evidence_evaluated doesn't spam the customer.
    private readonly ConcurrentDictionary<string, byte> _missingEvidenceEmailed = new();

    public ChannelReader<string> Subscribe(string claimId)
    {
        var channel = _channels.GetOrAdd(claimId, _ =>
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true }));
        if (_latestEvent.TryGetValue(claimId, out var last))
            channel.Writer.TryWrite(last);
        return channel.Reader;
    }

    public void Publish(string claimId, string eventJson)
    {
        _latestEvent[claimId] = eventJson;
        if (_channels.TryGetValue(claimId, out var channel))
            channel.Writer.TryWrite(eventJson);
    }

    public void Complete(string claimId)
    {
        if (_channels.TryRemove(claimId, out var channel))
            channel.Writer.TryComplete();
        // Keep _latestEvent so the next reconnect still catches up.
    }

    /// <summary>Atomically marks the claim as having received its missing-evidence
    /// email. Returns true on the first call, false on subsequent calls.</summary>
    public bool TryMarkMissingEvidenceEmailed(string claimId)
        => _missingEvidenceEmailed.TryAdd(claimId, 1);
}
