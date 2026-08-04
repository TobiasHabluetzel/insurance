using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Insurance.Api.Services;

namespace Insurance.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClaimWizardController(
    PolicySystemService policySystem,
    IHttpClientFactory httpClientFactory,
    Insurance.Api.Services.ClaimEventService claimEvents,
    Insurance.Api.Services.ClaimClassificationService classifier,
    Insurance.Api.Services.EmailSender emailSender,
    Insurance.Api.Services.ShortCodeService shortCodes,
    IConfiguration config,
    ILogger<ClaimWizardController> logger) : ControllerBase
{
    [HttpGet("search-policies")]
    public async Task<IActionResult> SearchPolicies([FromQuery] string email, CancellationToken ct)
    {
        var policies = await policySystem.FindByEmailAsync(email, ct);
        var holderPolicies = policies
            .Where(p => p.Holder is not null && string.Equals(p.Holder.Email, email, StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.Equals(p.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .Select(p => new
            {
                clientId = p.Holder!.Id,
                clientName = $"{p.Holder.FirstName} {p.Holder.LastName}".Trim(),
                policyId = p.Id,
                policyNumber = p.DisplayNumber,
                productName = p.ProductName,
                situationId = p.Id,
                currencyCode = p.CurrencyCode,
                periodStart = p.PeriodStart,
                periodEnd = p.PeriodEnd,
            });
        return Ok(holderPolicies);
    }

    [HttpGet("search-by-number")]
    public async Task<IActionResult> SearchByNumber([FromQuery] string number, CancellationToken ct)
    {
        var policy = await policySystem.FindByDisplayNumberAsync(number, ct);
        if (policy is null) return NotFound();

        var holderName = policy.Holder is null
            ? ""
            : $"{policy.Holder.FirstName} {policy.Holder.LastName}".Trim();

        return Ok(new
        {
            policyId = policy.Id,
            policyNumber = policy.DisplayNumber,
            holderName,
            situationId = policy.Id,
        });
    }

    [HttpGet("dashboard/{policyId:guid}")]
    public async Task<IActionResult> GetDashboard(Guid policyId, CancellationToken ct)
    {
        var policy = await policySystem.GetByIdAsync(policyId, ct);
        if (policy is null) return NotFound();

        var holderName = policy.Holder is null
            ? ""
            : $"{policy.Holder.FirstName} {policy.Holder.LastName}".Trim();
        var holderFirstName = policy.Holder?.FirstName ?? "";

        var insured = policy.Insureds.Select(i => new
        {
            id = i.Id,
            uid = i.Id.ToString(),
            name = $"{i.FirstName} {i.LastName}".Trim(),
            firstName = i.FirstName,
            lastName = i.LastName,
            email = i.Email,
            phone = i.PhoneNumber,
            dateOfBirth = i.DateOfBirth,
            primaryAddress = (object?)null,
        }).ToList();

        return Ok(new
        {
            policyId = policy.Id,
            policyNumber = policy.DisplayNumber,
            holderName,
            holderFirstName,
            productName = policy.ProductName,
            situationId = policy.Id,
            currencyCode = policy.CurrencyCode,
            periodStart = policy.PeriodStart,
            periodEnd = policy.PeriodEnd,
            destination = policy.Destination,
            // Claims are not tracked in the demo system yet.
            claimsCount = 0,
            insured,
            // AC-shape policyCoverage straight from the policy system — the
            // frontend forwards this to AC unchanged.
            policyCoverage = policy.PolicyCoverage,
        });
    }

    [HttpPost("submit-claim")]
    public async Task<IActionResult> SubmitClaim([FromBody] JsonElement body, CancellationToken ct)
    {
        // Generate our human-readable short code and inject it as AC's externalId,
        // overriding whatever the frontend posted (frontend used the policy number
        // there as a placeholder; the actual policy reference lives on
        // policyCoverage.reference and is unaffected).
        var shortCode = shortCodes.Generate();
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body.GetRawText())!;
        var enriched = new Dictionary<string, object?>();
        foreach (var kv in dict)
        {
            if (string.Equals(kv.Key, "externalId", StringComparison.OrdinalIgnoreCase)) continue;
            enriched[kv.Key] = kv.Value;
        }
        enriched["externalId"] = shortCode;

        var client = httpClientFactory.CreateClient("ClaimsApi");
        var content = new StringContent(JsonSerializer.Serialize(enriched), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("claims", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(json);
            // AC has two shapes: POST /claims returns the claim at root, but
            // POST /claims/upload returns { claim: {...}, document: {...} }.
            // Tolerate both so we always grab the id.
            var root = doc.RootElement;
            var claimNode = root.TryGetProperty("claim", out var c) && c.ValueKind == JsonValueKind.Object
                ? c
                : root;
            if (claimNode.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                var claimId = idProp.GetString()!;

                // Mirror the email flow — fire a confirmation email so the
                // customer has the reference in writing. The claim record
                // itself is owned by the claims-ops service, which hydrates
                // from AC's reviewed webhook.
                try
                {
                    var claimant = claimNode.TryGetProperty("claimant", out var cl) ? cl : default;
                    var toEmail = claimant.ValueKind == JsonValueKind.Object
                        && claimant.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String
                        ? em.GetString() : null;
                    var firstName = claimant.ValueKind == JsonValueKind.Object
                        && claimant.TryGetProperty("firstName", out var fn) && fn.ValueKind == JsonValueKind.String
                        ? fn.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(toEmail))
                    {
                        var baseUrl = config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                        var claimUrl = $"{baseUrl.TrimEnd('/')}/claim/{shortCode}";
                        await emailSender.SendClaimReceivedAsync(toEmail!, firstName, shortCode, claimUrl, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[wizard] confirmation email failed for claim {Claim}", claimId);
                }
            }
            else
            {
                logger.LogWarning("[wizard] AC submit-claim succeeded but response had no id field. Body: {Body}", json);
            }
        }
        return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(json));
    }

    [HttpPut("claims/{claimId}")]
    public async Task<IActionResult> UpdateClaim(string claimId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ClaimsApi");
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"claims/{claimId}", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(json));
    }

    [HttpPost("claims/{claimId}/documents")]
    public async Task<IActionResult> UploadDocument(string claimId, IFormFile file, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ClaimsApi");
        using var form = new MultipartFormDataContent();
        using var stream = file.OpenReadStream();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        form.Add(fileContent, "file", file.FileName);
        var response = await client.PostAsync($"claims/{claimId}/documents", form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(json));
    }

    [HttpPost("claims/{claimId}/evidence-requests/{evidenceRequestId}/report-missing-evidence")]
    public async Task<IActionResult> ReportMissingEvidence(string claimId, string evidenceRequestId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ClaimsApi");
        var content = new StringContent("{\"message\":\"Customer reported evidence not available\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"claims/{claimId}/evidence-requests/{evidenceRequestId}/report-missing-evidence", content, ct);
        return StatusCode((int)response.StatusCode);
    }

    [HttpGet("claims/{claimId}/stream")]
    public async Task StreamClaimEvents(string claimId, CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var reader = claimEvents.Subscribe(claimId);
        try
        {
            await foreach (var json in reader.ReadAllAsync(ct))
            {
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        finally
        {
            claimEvents.Complete(claimId);
        }
    }

    public record BehaviorAttachRequest(string AcClaimId, string Text, string? Stage, long? PriorSubmittedAt);

    /// <summary>
    /// Emails the behavioral signal log to the address configured under
    /// Behavior:EmailTo. The .jsonl payload goes in the body as plain text so
    /// we can copy-paste it from the inbox. Stage names the post point
    /// ("submit", "evidence", …). When the evidence drop arrives with a
    /// PriorSubmittedAt (epoch ms of the initial registration), we surface
    /// the elapsed-since-submit time in the email header so the analyst
    /// doesn't have to diff timestamps by hand.
    /// </summary>
    [HttpPost("behavior")]
    public async Task<IActionResult> AttachBehavior([FromBody] BehaviorAttachRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.AcClaimId)) return BadRequest();

        var to = config["Behavior:EmailTo"];
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("[behavior] Behavior:EmailTo not configured — dropping {Stage} log for {Claim}",
                req.Stage ?? "submit", req.AcClaimId);
            return Ok();
        }

        var stage = string.IsNullOrWhiteSpace(req.Stage) ? "submit" : req.Stage;
        var subject = $"Behavior signal · {stage} · {req.AcClaimId}";
        var elapsedLine = "";
        if (req.PriorSubmittedAt is long prior && prior > 0)
        {
            var submittedAt = DateTimeOffset.FromUnixTimeMilliseconds(prior);
            var elapsed = DateTimeOffset.UtcNow - submittedAt;
            var humanElapsed = elapsed.TotalSeconds < 60
                ? $"{(int)elapsed.TotalSeconds}s"
                : elapsed.TotalMinutes < 60
                    ? $"{elapsed.TotalMinutes:F1} min"
                    : $"{elapsed.TotalHours:F1} h";
            elapsedLine = $"<p>Time from claim registration to this drop: <strong>{humanElapsed}</strong>"
                + $" (submitted at {submittedAt:u})</p>";
        }
        // Wrap the JSON-lines in a <pre> so Resend's HTML renderer leaves it
        // alone. No CSS — easier to grep / paste straight into a tool.
        var html = $"<p>Stage: <strong>{System.Net.WebUtility.HtmlEncode(stage)}</strong></p>"
            + $"<p>AC claim id: <code>{System.Net.WebUtility.HtmlEncode(req.AcClaimId)}</code></p>"
            + elapsedLine
            + "<pre style=\"font-family: monospace; font-size: 12px; white-space: pre-wrap;\">"
            + System.Net.WebUtility.HtmlEncode(req.Text ?? "")
            + "</pre>";

        try { await emailSender.SendRawAsync(to, subject, html, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[behavior] email send failed for {Claim}", req.AcClaimId);
        }
        return Ok();
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] JsonElement payload, CancellationToken ct)
    {
        var eventType = payload.TryGetProperty("eventType", out var et) ? et.GetString() : null;

        var claimId = payload.TryGetProperty("data", out var data)
            && data.TryGetProperty("id", out var id)
            ? id.GetString() : null;

        Console.WriteLine($"[webhook] eventType={eventType} claimId={claimId}");

        if (string.IsNullOrEmpty(claimId)) return Ok();

        // Classify the incident and inject category into the payload before publishing
        var description = data.TryGetProperty("incident", out var inc)
            && inc.TryGetProperty("description", out var desc)
            ? desc.GetString() : null;

        var category = string.IsNullOrEmpty(description)
            ? "other"
            : await classifier.ClassifyAsync(description, ct);

        // Rebuild JSON with category added
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload.GetRawText())!;
        var enriched = new Dictionary<string, object?>();
        foreach (var kv in dict) enriched[kv.Key] = kv.Value;
        enriched["category"] = category;

        claimEvents.Publish(claimId, JsonSerializer.Serialize(enriched));

        // On the first claim.evidence_evaluated event for a claim, email the
        // customer a list of what's still missing. Dedupe via ClaimEventService
        // so subsequent re-fires don't spam.
        if (eventType == "claim.evidence_evaluated"
            && claimEvents.TryMarkMissingEvidenceEmailed(claimId))
        {
            await TrySendMissingEvidenceEmailAsync(claimId, data, ct);
        }

        // Hydration of the local Claim row is now owned by ClaimsOps — point
        // AC's reviewed-subscription at that service instead of this one.

        return Ok();
    }

    private async Task TrySendMissingEvidenceEmailAsync(string claimId, JsonElement data, CancellationToken ct)
    {
        try
        {
            var pending = new List<Insurance.Api.Services.EvidenceItemSummary>();
            if (data.TryGetProperty("evidenceRequests", out var requests) && requests.ValueKind == JsonValueKind.Array)
            {
                foreach (var req in requests.EnumerateArray())
                {
                    var request = req.TryGetProperty("request", out var rq) ? rq.GetString() : null;
                    var isMissing = req.TryGetProperty("isMissing", out var im) && im.ValueKind == JsonValueKind.True;
                    var hasEvidence = req.TryGetProperty("evidence", out var ev)
                        && ev.ValueKind == JsonValueKind.Array
                        && ev.GetArrayLength() > 0;
                    if (string.IsNullOrWhiteSpace(request) || isMissing || hasEvidence) continue;
                    if (string.Equals(request, "insurance policy", StringComparison.OrdinalIgnoreCase)) continue;
                    var description = req.TryGetProperty("description", out var ds) ? ds.GetString() : null;
                    pending.Add(new Insurance.Api.Services.EvidenceItemSummary(request!, description));
                }
            }

            if (pending.Count == 0)
            {
                logger.LogInformation("[webhook] no pending evidence for claim {Claim} — skipping missing-evidence email", claimId);
                return;
            }

            string? toEmail = null;
            string firstName = "";
            if (data.TryGetProperty("claimant", out var claimant) && claimant.ValueKind == JsonValueKind.Object)
            {
                toEmail = claimant.TryGetProperty("email", out var em) ? em.GetString() : null;
                firstName = (claimant.TryGetProperty("firstName", out var fn) ? fn.GetString() : null) ?? "";
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                logger.LogWarning("[webhook] no claimant email on claim {Claim} — cannot send missing-evidence email", claimId);
                return;
            }

            var baseUrl = config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            var claimUrl = $"{baseUrl.TrimEnd('/')}/claim/{claimId}/evidence";
            // Prefer our short code (stored on AC as externalId) so the email
            // shows the same reference the customer received on confirmation.
            var externalId = data.TryGetProperty("externalId", out var ext) && ext.ValueKind == JsonValueKind.String
                ? ext.GetString()
                : null;
            var reference = !string.IsNullOrEmpty(externalId) ? externalId : claimId;
            await emailSender.SendMissingEvidenceAsync(toEmail, firstName, reference, claimUrl, pending, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[webhook] failed to send missing-evidence email for claim {Claim}", claimId);
        }
    }

    [HttpGet("policy-situation/{policyId:guid}")]
    public async Task<IActionResult> GetPolicySituation(Guid policyId, CancellationToken ct)
    {
        var policy = await policySystem.GetByIdAsync(policyId, ct);
        if (policy is null) return NotFound();

        var insured = policy.Insureds.Select(i => new
        {
            id = i.Id,
            uid = i.Id.ToString(),
            name = $"{i.FirstName} {i.LastName}".Trim(),
        }).ToList();

        var holderName = policy.Holder is null
            ? ""
            : $"{policy.Holder.FirstName} {policy.Holder.LastName}".Trim();

        return Ok(new
        {
            policyId = policy.Id,
            policyNumber = policy.DisplayNumber,
            situationId = policy.Id,
            currencyCode = policy.CurrencyCode,
            productName = policy.ProductName,
            holderName,
            insured,
        });
    }
}
