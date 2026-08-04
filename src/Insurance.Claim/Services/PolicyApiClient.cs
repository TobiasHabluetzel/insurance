using System.Text.Json;
using System.Text.Json.Nodes;

namespace Insurance.Claim.Services;

/// <summary>
/// Thin server-side client for the Insurance.Api claim-wizard endpoints. The
/// React app hits these same routes directly from the browser — the MVC
/// port calls them from the controller instead so we can render the view
/// server-side (and keep the API base URL off the client).
/// </summary>
public class PolicyApiClient(IHttpClientFactory factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PolicySearchResult?> SearchByNumberAsync(string number, CancellationToken ct)
    {
        var client = factory.CreateClient("Api");
        var response = await client.GetAsync(
            $"api/claimwizard/search-by-number?number={Uri.EscapeDataString(number)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PolicySearchResult>(JsonOpts, ct);
    }

    /// <summary>
    /// Fetch the dashboard payload for a policy. Returns the raw JsonNode so
    /// the caller can pick whichever fields the view needs without us
    /// duplicating every field into a strongly-typed record (the payload
    /// mixes shallow scalars with the deeply nested policyCoverage tree).
    /// </summary>
    public async Task<JsonNode?> GetDashboardAsync(string policyId, CancellationToken ct)
    {
        var client = factory.CreateClient("Api");
        var response = await client.GetAsync(
            $"api/claimwizard/dashboard/{Uri.EscapeDataString(policyId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct);
    }
}

public record PolicySearchResult(string PolicyId, string PolicyNumber, string HolderName, string SituationId);
