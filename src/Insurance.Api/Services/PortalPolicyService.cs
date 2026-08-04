using System.Text.Json;
using System.Text.Json.Serialization;

namespace Insurance.Api.Services;

public record InsuredLookup(Guid Id, string FirstName, string LastName, string Email);

public record PolicyTraveler(string FirstName, string LastName, bool IsHolder);

public record InsuredPolicy(
    Guid Id,
    string DisplayNumber,
    string ProductName,
    string PeriodStart,
    string PeriodEnd,
    string? Destination,
    string Status,
    bool IsHolder,
    IReadOnlyList<PolicyTraveler> Travelers);

public record InsuredDashboard(
    Guid Id,
    string FirstName,
    string LastName,
    IReadOnlyList<InsuredPolicy> Policies);

public class PortalPolicyService(IHttpClientFactory httpClientFactory, ILogger<PortalPolicyService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Looks up an insured by exact email. Returns null when the policy
    /// system has nobody on that address — that maps to "no policy on file".
    /// </summary>
    public async Task<InsuredLookup?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PolicySystem");
        var r = await client.GetAsync($"/api/admin/insureds/by-email?email={Uri.EscapeDataString(email.Trim())}", ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
        {
            logger.LogWarning("[policy-system] by-email lookup failed status={Status}", (int)r.StatusCode);
            return null;
        }
        var json = await r.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json).RootElement;
        return new InsuredLookup(
            Id: doc.GetProperty("id").GetGuid(),
            FirstName: doc.GetProperty("firstName").GetString() ?? "",
            LastName: doc.GetProperty("lastName").GetString() ?? "",
            Email: doc.GetProperty("email").GetString() ?? "");
    }

    /// <summary>Insured + their policies, used for the dashboard.</summary>
    public async Task<InsuredDashboard?> GetDashboardAsync(Guid insuredId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PolicySystem");
        var r = await client.GetAsync($"/api/admin/insureds/{insuredId}", ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
        {
            logger.LogWarning("[policy-system] dashboard lookup failed status={Status}", (int)r.StatusCode);
            return null;
        }
        var json = await r.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json).RootElement;
        var policies = new List<InsuredPolicy>();
        if (doc.TryGetProperty("policies", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                var travelers = new List<PolicyTraveler>();
                if (p.TryGetProperty("insureds", out var ins) && ins.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in ins.EnumerateArray())
                    {
                        travelers.Add(new PolicyTraveler(
                            FirstName: t.TryGetProperty("firstName", out var fn) ? fn.GetString() ?? "" : "",
                            LastName: t.TryGetProperty("lastName", out var ln) ? ln.GetString() ?? "" : "",
                            IsHolder: t.TryGetProperty("isHolder", out var ih) && ih.GetBoolean()));
                    }
                }
                policies.Add(new InsuredPolicy(
                    Id: p.GetProperty("id").GetGuid(),
                    DisplayNumber: p.GetProperty("displayNumber").GetString() ?? "",
                    ProductName: BuildProductName(p),
                    PeriodStart: p.GetProperty("periodStart").GetString() ?? "",
                    PeriodEnd: p.GetProperty("periodEnd").GetString() ?? "",
                    Destination: p.TryGetProperty("destination", out var dest) ? dest.GetString() : null,
                    Status: p.GetProperty("status").GetString() ?? "Active",
                    IsHolder: p.TryGetProperty("isHolder", out var h) && h.GetBoolean(),
                    Travelers: travelers));
            }
        }
        return new InsuredDashboard(
            Id: doc.GetProperty("id").GetGuid(),
            FirstName: doc.GetProperty("firstName").GetString() ?? "",
            LastName: doc.GetProperty("lastName").GetString() ?? "",
            Policies: policies);
    }

    private static string BuildProductName(JsonElement p)
    {
        var tier = p.TryGetProperty("tier", out var t) ? t.GetString() : null;
        var duration = p.TryGetProperty("duration", out var d) ? d.GetString() : null;
        var type = p.TryGetProperty("type", out var ty) ? ty.GetString() : null;
        var prettyDuration = duration switch
        {
            "SingleTrip" => "Single Trip",
            "Annual" => "Annual",
            _ => duration ?? "",
        };
        return $"{tier} {prettyDuration} {type}".Trim();
    }

    /// <summary>Verify the insured holds the given policy (used during disambiguation).</summary>
    public async Task<bool> InsuredHoldsPolicyAsync(Guid insuredId, string policyNumber, CancellationToken ct)
    {
        var dashboard = await GetDashboardAsync(insuredId, ct);
        if (dashboard is null) return false;
        return dashboard.Policies.Any(p => p.DisplayNumber == policyNumber && p.IsHolder);
    }

    // Kept to keep the JSON helpers handy.
    public JsonSerializerOptions Json => JsonOpts;
}
