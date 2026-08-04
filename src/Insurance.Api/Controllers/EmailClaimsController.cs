using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Insurance.Api.Services;

namespace Insurance.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailClaimsController(
    EmailSender emailSender,
    ShortCodeService shortCodes,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<EmailClaimsController> logger) : ControllerBase
{
    // UUID v4/v7 pattern AC uses for claim ids. Used to detect customer replies
    // by parsing the reference out of the subject line.
    private static readonly Regex ClaimIdPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);
    // Our human-readable short codes: <PREFIX>-<6 chars from the Crockford-ish alphabet>
    private static readonly Regex ShortCodePattern = new(
        @"\b[A-Z]{2,5}-[A-HJKMNPQRSTVWXYZ2-9]{6}\b",
        RegexOptions.Compiled);

    // SendGrid Inbound Parse posts multipart/form-data
    [HttpPost("ingest")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public async Task<IActionResult> Ingest([FromForm] IFormCollection form, CancellationToken ct)
    {
        logger.LogInformation("[email-ingest] received form fields={Fields} files={Files}",
            string.Join(",", form.Keys), form.Files.Count);

        // -- 1. Parse sender -------------------------------------------------
        // Field names differ between providers:
        //   SendGrid Inbound Parse: from, subject, text, html, attachments + attachment{N}
        //   CloudMailin Multipart Normalized: envelope[from], headers[subject], plain, html, attachments[]
        // Prefer fields that include the display name. CloudMailin's
        // envelope[from] is just the bare email address; headers[from] has
        // the "Display Name <email>" form.
        var from = FirstNonEmpty(form, "from", "headers[from]", "envelope[from]");
        var (senderEmail, senderFirstName, senderLastName) = ParseSender(from);
        if (string.IsNullOrWhiteSpace(senderFirstName) && string.IsNullOrWhiteSpace(senderLastName))
        {
            (senderFirstName, senderLastName) = NameFromEmailLocalPart(senderEmail);
        }
        logger.LogInformation("[email-ingest] sender email={Email} first={First} last={Last}",
            senderEmail, senderFirstName, senderLastName);

        var subject = FirstNonEmpty(form, "subject", "headers[subject]");
        var plain = FirstNonEmpty(form, "text", "plain");
        var html = FirstNonEmpty(form, "html");
        var body = !string.IsNullOrEmpty(plain) ? plain : StripHtml(html);
        logger.LogInformation("[email-ingest] subject='{Subject}' bodyLen={Len}", subject, body.Length);

        // -- 1b. Detect customer reply ---------------------------------------
        // Outbound emails put our short reference (e.g. CLM-F9KX7T) in the
        // subject. With the claim record now living in ClaimsOps, the
        // short-code → AC-id lookup would require a cross-service call;
        // skip that for now and match the raw AC UUID if the subject has
        // one (older deploys, AC's own emails, etc.).
        var uuidMatch = ClaimIdPattern.Match(subject);
        if (uuidMatch.Success)
        {
            logger.LogInformation("[email-ingest] subject references claim {Claim} — treating as reply", uuidMatch.Value);
            return await HandleReplyAsync(uuidMatch.Value, form.Files, ct);
        }

        var firstName = senderFirstName;

        // -- 2. Generate a human-readable short code (CLM-F9KX7T) ----------
        // Pass it as AC's externalId, then persist {shortCode -> claimId} so
        // we can resolve replies and chat lookups later.
        var shortCode = shortCodes.Generate();

        // -- 3. Create claim by uploading the email body as a text document --
        // AC parses the structured "Claimant / Incident" sections out of the
        // body and creates the claim itself; we just enrich with the sender's
        // email at the top so AC always has a way to contact the claimant.
        // Note: AC matches the file's Content-Type strictly — no charset
        // suffix, or the upload is rejected with "Unsupported file type".
        var claimsClient = httpClientFactory.CreateClient("ClaimsApi");
        var textContent = $"Email: {senderEmail}\n\n{body}";
        logger.LogInformation("[email-ingest] uploading body ({Len} chars) to AC /claims/upload, code={Code}",
            textContent.Length, shortCode);

        using var uploadContent = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes(textContent);
        var uploadFileContent = new ByteArrayContent(fileBytes);
        uploadFileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(uploadFileContent, "file", "claim.txt");

        var claimResponse = await claimsClient.PostAsync(
            $"claims/upload?externalId={Uri.EscapeDataString(shortCode)}",
            uploadContent, ct);
        if (!claimResponse.IsSuccessStatusCode)
        {
            var err = await claimResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("[email-ingest] AC POST /claims/upload failed status={Status} body={Body}",
                (int)claimResponse.StatusCode, err);
            return StatusCode((int)claimResponse.StatusCode, err);
        }

        var claimResult = await claimResponse.Content.ReadAsStringAsync(ct);
        using var claimDoc = JsonDocument.Parse(claimResult);
        // The upload endpoint returns { claim: {...}, document: {...} }; fall
        // back to a top-level id if AC ever changes the shape.
        var root = claimDoc.RootElement;
        var claimNode = root.TryGetProperty("claim", out var c) ? c : root;
        var claimId = claimNode.GetProperty("id").GetString()!;
        logger.LogInformation("[email-ingest] AC claim created id={Id} code={Code}", claimId, shortCode);

        // The Claim record itself is owned by ClaimsOps and hydrated from
        // AC's reviewed webhook; nothing to persist locally.
        var claimRef = shortCode;

        // /claims/upload silently drops externalId and productCode when passed
        // as query params — PUT them back onto the claim so AC's webhook
        // payload + ClaimsOps inbox carry our short code and the demo product.
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                externalId = shortCode,
                productCode = "salsin_v3",
            });
            var patchBody = new StringContent(payload, Encoding.UTF8, "application/json");
            var patchRes = await claimsClient.PutAsync($"claims/{claimId}", patchBody, ct);
            if (!patchRes.IsSuccessStatusCode)
            {
                var err = await patchRes.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[email-ingest] PUT externalId+product on claim {Claim} failed {Status}: {Body}",
                    claimId, (int)patchRes.StatusCode, err);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[email-ingest] PUT externalId+product on claim {Claim} threw", claimId);
        }

        // -- 5. Upload attachments ------------------------------------------
        // Iterate every file part regardless of field name — works for both
        // SendGrid (attachment1, attachment2…) and CloudMailin (attachments[]).
        logger.LogInformation("[email-ingest] uploading {Count} attachment(s)", form.Files.Count);
        foreach (var file in form.Files)
        {
            try
            {
                using var multipart = new MultipartFormDataContent();
                using var stream = file.OpenReadStream();
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                multipart.Add(fileContent, "file", file.FileName);
                var docResp = await claimsClient.PostAsync($"claims/{claimId}/documents", multipart, ct);
                if (!docResp.IsSuccessStatusCode)
                {
                    var docErr = await docResp.Content.ReadAsStringAsync(ct);
                    logger.LogError("[email-ingest] document upload {Name} failed status={Status} body={Body}",
                        file.FileName, (int)docResp.StatusCode, docErr);
                }
                else
                {
                    logger.LogInformation("[email-ingest] uploaded {Name} ({Size} bytes)", file.FileName, file.Length);
                }
            }
            catch (Exception ex2)
            {
                logger.LogError(ex2, "[email-ingest] document upload {Name} threw", file.FileName);
            }
        }

        // -- 6. Send confirmation via Resend --------------------------------
        var baseUrl = config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var claimUrl = $"{baseUrl.TrimEnd('/')}/claim/{claimId}/evidence";
        await emailSender.SendClaimReceivedAsync(senderEmail, firstName, claimRef!, claimUrl, ct);

        return Ok(new { claimId, reference = claimRef });
    }

    private async Task<IActionResult> HandleReplyAsync(string claimId, IFormFileCollection files, CancellationToken ct)
    {
        var claimsClient = httpClientFactory.CreateClient("ClaimsApi");

        // Verify the claim actually exists before uploading anything — saves
        // us from creating orphan docs if the subject contained a stray UUID.
        var check = await claimsClient.GetAsync($"claims/{claimId}", ct);
        if (!check.IsSuccessStatusCode)
        {
            logger.LogWarning("[email-ingest] reply claim {Claim} not found in AC (status={Status}) — ignoring",
                claimId, (int)check.StatusCode);
            return Ok(new { ignored = true, reason = "claim-not-found" });
        }

        logger.LogInformation("[email-ingest] uploading {Count} reply attachment(s) to claim {Claim}", files.Count, claimId);
        var uploaded = 0;
        foreach (var file in files)
        {
            try
            {
                using var multipart = new MultipartFormDataContent();
                using var stream = file.OpenReadStream();
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                multipart.Add(fileContent, "file", file.FileName);
                var docResp = await claimsClient.PostAsync($"claims/{claimId}/documents", multipart, ct);
                if (!docResp.IsSuccessStatusCode)
                {
                    var docErr = await docResp.Content.ReadAsStringAsync(ct);
                    logger.LogError("[email-ingest] reply doc {Name} failed status={Status} body={Body}",
                        file.FileName, (int)docResp.StatusCode, docErr);
                }
                else
                {
                    logger.LogInformation("[email-ingest] uploaded reply doc {Name} ({Size} bytes)", file.FileName, file.Length);
                    uploaded++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[email-ingest] reply doc {Name} threw", file.FileName);
            }
        }
        return Ok(new { claimId, replyAttachments = uploaded });
    }

    private static (string email, string firstName, string lastName) ParseSender(string from)
    {
        // Handles "Display Name <email@example.com>" or just "email@example.com"
        var match = Regex.Match(from, @"^(?:""?([^""<]+)""?\s*)?<([^>]+)>");
        if (match.Success)
        {
            var email = match.Groups[2].Value.Trim();
            var displayName = match.Groups[1].Value.Trim();
            var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return (email, parts.Length > 0 ? parts[0] : "", parts.Length > 1 ? parts[1] : "");
        }

        // Plain email address, no display name
        return (from.Trim(), "", "");
    }

    private static string StripHtml(string html)
    {
        return Regex.Replace(html, "<[^>]*>", " ").Trim();
    }

    private static string FirstNonEmpty(IFormCollection form, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = form[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return "";
    }

    private static (string firstName, string lastName) NameFromEmailLocalPart(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return ("", "");
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        // Split on common separators (.  _  -)
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();
        if (parts.Length == 0) return ("", "");
        if (parts.Length == 1) return (parts[0], "");
        return (parts[0], string.Join(' ', parts[1..]));
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
