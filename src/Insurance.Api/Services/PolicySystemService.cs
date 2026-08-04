using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Insurance.Api.Services;

/// <summary>
/// Talks to the policy demo system (policyclaimdummy) over REST. Replaces the
/// old NIS GraphQL client — claims app reads policies and coverages from here.
/// </summary>
public class PolicySystemService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PolicyRecord?> FindByDisplayNumberAsync(string displayNumber, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PolicySystem");
        var res = await client.GetAsync($"api/policies/search?number={Uri.EscapeDataString(displayNumber)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<PolicyRecord[]>(json, JsonOpts);
        return list?.FirstOrDefault();
    }

    public async Task<List<PolicyRecord>> FindByEmailAsync(string email, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PolicySystem");
        var res = await client.GetAsync($"api/policies/search?email={Uri.EscapeDataString(email)}", ct);
        if (!res.IsSuccessStatusCode) return new();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<PolicyRecord>>(json, JsonOpts) ?? new();
    }

    public async Task<PolicyRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PolicySystem");
        var res = await client.GetAsync($"api/policies/{id}", ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PolicyRecord>(json, JsonOpts);
    }
}

public record PolicyRecord(
    Guid Id,
    string DisplayNumber,
    string Tier,
    string Duration,
    string Type,
    string PeriodStart,
    string PeriodEnd,
    string? Destination,
    string CurrencyCode,
    string? CancelledAt,
    string Status,
    string ProductName,
    InsuredRecord? Holder,
    InsuredRecord[] Insureds,
    // Captured as an owned JsonNode tree so the AC-shape policyCoverage
    // round-trips back out through the dashboard response unchanged.
    JsonNode? PolicyCoverage);

public record InsuredRecord(
    Guid Id,
    string FirstName,
    string LastName,
    string DateOfBirth,
    string Email,
    string? PhoneNumber,
    string? IdentityNumber = null,
    [property: JsonPropertyName("isHolder")] bool IsHolder = false);
