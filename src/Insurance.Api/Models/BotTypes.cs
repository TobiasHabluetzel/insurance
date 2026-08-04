using System.Text.Json.Serialization;

namespace Insurance.Api.Models;

/// <summary>
/// Where we are in the bot conversation. Client mirrors the same names.
/// </summary>
public enum BotStep
{
    Welcome,
    AskDescription,
    AskWhen,
    AskWhere,
    AskEmail,
    AskDob,
    SelectPolicy,
    SelectInsured,
    AskDocuments,
    Confirm,
    Submitting,
    Done,
    Blocked,
}

public record BotPolicyCandidate(
    Guid PolicyId,
    string DisplayNumber,
    string ProductName,
    string HolderName,
    string? PersonId,
    string? DateOfBirth,
    string? IdentityNumber = null);

public record BotInsuredCandidate(
    string Uid,
    string Name,
    string? DateOfBirth,
    string? Email);

/// <summary>
/// Conversation state passed back and forth between client and server. Kept
/// small and serialisable so we don't need server-side session storage for v1.
/// </summary>
public class BotState
{
    public BotStep Step { get; set; } = BotStep.Welcome;
    public string? Description { get; set; }
    public string? IncidentDate { get; set; }
    public string? Location { get; set; }
    public string? Email { get; set; }

    /// <summary>People (by email) whose DOB we'll check the user's input against.</summary>
    public List<BotPolicyCandidate> Candidates { get; set; } = new();

    public Guid? SelectedPolicyId { get; set; }
    public string? SelectedPersonId { get; set; }

    /// <summary>Travelers / insured persons on the selected policy.</summary>
    public List<BotInsuredCandidate> Insured { get; set; } = new();

    /// <summary>The insured the user picked as the actual claimant.</summary>
    public string? SelectedInsuredUid { get; set; }
    public string? SelectedInsuredName { get; set; }
    public string? SelectedInsuredDob { get; set; }

    public int VerifyAttempts { get; set; } = 0;

    public string? ClaimId { get; set; }
    public string? ShortCode { get; set; }
}

public class BotMessageRequest
{
    public string Message { get; set; } = "";
    public BotState State { get; set; } = new();
}

public class BotMessageResponse
{
    [JsonPropertyName("reply")]
    public string Reply { get; set; } = "";

    /// <summary>Optional quick-reply options for the next user turn.</summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    [JsonPropertyName("state")]
    public BotState State { get; set; } = new();

    /// <summary>Set when the bot is now expecting file uploads from the user.</summary>
    [JsonPropertyName("awaitingUpload")]
    public bool AwaitingUpload { get; set; }

    /// <summary>Set on the final Done step so the client can navigate or display the reference.</summary>
    [JsonPropertyName("shortCode")]
    public string? ShortCode { get; set; }
}
