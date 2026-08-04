using System.Text.Json.Nodes;

namespace Insurance.Claim.Models;

/// <summary>
/// The shape the Dashboard view actually reads from. The React version
/// carried the full policy blob around via context; we resolve everything
/// server-side on each request instead — simpler, and the API is cheap.
/// </summary>
public class DashboardViewModel
{
    public required string PolicyId { get; init; }
    public required string PolicyNumber { get; init; }
    public string HolderFirstName { get; init; } = "";
    public string HolderName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public int ClaimsCount { get; init; }

    /// <summary>Passed straight to the FileClaim view — the full JSON tree the API returns.</summary>
    public required JsonNode RawPolicy { get; init; }
}
