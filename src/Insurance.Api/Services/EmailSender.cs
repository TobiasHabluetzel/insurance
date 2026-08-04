using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Insurance.Api.Services;

public record EvidenceItemSummary(string Request, string? Description);

public class EmailSender(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<EmailSender> logger)
{
    public Task<bool> SendClaimReceivedAsync(
        string toEmail, string firstName, string reference, string claimUrl, CancellationToken ct)
    {
        var html = $"""
            <p>Dear {Encode(firstName)},</p>
            <p>We have received your insurance claim. Your reference number is: <strong>{Encode(reference)}</strong></p>
            <p>We've started reviewing your documents. To make sure we can settle your claim quickly, please open it in our portal — there you can add any missing evidence and fill in details we couldn't extract from your email automatically.</p>
            <p><a href="{claimUrl}" style="display:inline-block;background:#0C3B5D;color:#fff;text-decoration:none;padding:12px 22px;border-radius:9999px;font-weight:600;">Open your claim</a></p>
            <p style="color:#6b7280;font-size:12px;">If the button doesn't work, paste this link into your browser:<br/><span style="word-break:break-all">{claimUrl}</span></p>
            <p>Kind regards,<br/>The Insurance Company</p>
            """;
        return SendAsync(toEmail, $"Claim received – reference {reference}", html, ct);
    }

    public Task<bool> SendMissingEvidenceAsync(
        string toEmail, string firstName, string reference, string claimUrl,
        IReadOnlyList<EvidenceItemSummary> missing, CancellationToken ct)
    {
        var listHtml = string.Join(
            "",
            missing.Select(m =>
                $"<li><strong>{Encode(m.Request)}</strong>{(string.IsNullOrWhiteSpace(m.Description) ? "" : $" – {Encode(m.Description!)}")}</li>"));

        var html = $"""
            <p>Dear {Encode(firstName)},</p>
            <p>We've reviewed your claim (reference <strong>{Encode(reference)}</strong>) and to finish processing it we still need the following from you:</p>
            <ul>{listHtml}</ul>
            <p>You can either <a href="{claimUrl}">open your claim in our portal</a> and upload the documents there, or simply <strong>reply to this email</strong> and attach them — please keep the subject line so we can match your reply to the claim.</p>
            <p>Kind regards,<br/>The Insurance Company</p>
            """;

        return SendAsync(toEmail, $"Action needed – reference {reference}", html, ct);
    }

    /// <summary>Public passthrough so other services can send custom emails without duplicating the Resend wiring.</summary>
    public Task<bool> SendRawAsync(string toEmail, string subject, string html, CancellationToken ct)
        => SendAsync(toEmail, subject, html, ct);

    private async Task<bool> SendAsync(string toEmail, string subject, string html, CancellationToken ct)
    {
        var fromAddress = config["Resend:FromAddress"];
        if (string.IsNullOrEmpty(fromAddress) || string.IsNullOrEmpty(config["Resend:ApiKey"]))
        {
            logger.LogWarning("[email-send] Resend:FromAddress or Resend:ApiKey not configured — skipping email to {Email}", toEmail);
            return false;
        }

        var body = new Dictionary<string, object?>
        {
            ["from"] = fromAddress,
            ["to"] = new[] { toEmail },
            ["subject"] = subject,
            ["html"] = html,
        };
        // If we have a CloudMailin inbound address configured, set it as Reply-To
        // so customer replies route back to /api/emailclaims/ingest and we can
        // match them to the existing claim by parsing the UUID in the subject.
        var inboundAddress = config["CloudMailin:InboundAddress"];
        if (!string.IsNullOrEmpty(inboundAddress))
            body["reply_to"] = inboundAddress;

        var resendClient = httpClientFactory.CreateClient("Resend");
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await resendClient.SendAsync(request, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("[email-send] Resend POST /emails returned {Status}: {Body}",
                (int)resp.StatusCode, respBody);
            return false;
        }
        logger.LogInformation("[email-send] sent '{Subject}' to {Email}", subject, toEmail);
        return true;
    }

    private static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
