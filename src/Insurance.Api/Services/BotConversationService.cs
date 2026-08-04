using System.Globalization;
using System.Text;
using System.Text.Json;
using Insurance.Api.Models;

namespace Insurance.Api.Services;

public class BotConversationService(
    PolicySystemService policySystem,
    BotExtractionService extractor,
    ShortCodeService shortCodes,
    EmailSender emailSender,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IHttpContextAccessor httpContext,
    ILogger<BotConversationService> logger)
{
    private const int MaxVerifyAttempts = 3;

    /// <summary>Initial greeting on a fresh conversation.</summary>
    public BotMessageResponse Welcome()
    {
        return new BotMessageResponse
        {
            Reply = "Hi! I can help you file a new claim. Could you tell me in a sentence or two what happened?",
            State = new BotState { Step = BotStep.AskDescription },
        };
    }

    public async Task<BotMessageResponse> HandleMessageAsync(BotMessageRequest req, CancellationToken ct)
    {
        return req.State.Step switch
        {
            BotStep.Welcome or BotStep.AskDescription => await HandleDescriptionAsync(req, ct),
            BotStep.AskWhen => await HandleDescriptionAsync(req, ct),
            BotStep.AskWhere => await HandleDescriptionAsync(req, ct),
            BotStep.AskEmail => await HandleEmailAsync(req, ct),
            BotStep.AskDob => await HandleDobAsync(req, ct),
            BotStep.SelectPolicy => await HandleSelectPolicyAsync(req, ct),
            BotStep.SelectInsured => HandleSelectInsured(req),
            BotStep.AskDocuments => HandleAskDocuments(req),
            BotStep.Confirm => HandleConfirm(req),
            BotStep.Done or BotStep.Blocked => Echo(req, "This conversation is finished. Refresh to start a new one."),
            _ => Echo(req, "Sorry, I'm a little lost. Let's start over by refreshing."),
        };
    }

    // ---- Step handlers ------------------------------------------------------

    private async Task<BotMessageResponse> HandleDescriptionAsync(BotMessageRequest req, CancellationToken ct)
    {
        var known = new BotExtraction(req.State.Description, false, req.State.IncidentDate, req.State.Location);
        var ex = await extractor.ExtractAsync(known, req.Message, ct);

        req.State.Description = ex.Description;
        req.State.IncidentDate = ex.IncidentDate;
        req.State.Location = ex.Location;

        if (string.IsNullOrWhiteSpace(req.State.Description))
        {
            return new BotMessageResponse
            {
                Reply = "Could you describe what happened in a bit more detail?",
                State = With(req.State, BotStep.AskDescription),
            };
        }
        if (!ex.DescriptionComplete)
        {
            return new BotMessageResponse
            {
                Reply = "Could you give me a bit more detail about what happened?",
                State = With(req.State, BotStep.AskDescription),
            };
        }
        if (string.IsNullOrWhiteSpace(req.State.IncidentDate))
        {
            return new BotMessageResponse
            {
                Reply = "Got it. When did this happen?",
                State = With(req.State, BotStep.AskWhen),
            };
        }
        if (string.IsNullOrWhiteSpace(req.State.Location))
        {
            return new BotMessageResponse
            {
                Reply = "Thanks. Where did it happen?",
                State = With(req.State, BotStep.AskWhere),
            };
        }
        return new BotMessageResponse
        {
            Reply = "Thanks. What's the email address your policy is registered under?",
            State = With(req.State, BotStep.AskEmail),
        };
    }

    private async Task<BotMessageResponse> HandleEmailAsync(BotMessageRequest req, CancellationToken ct)
    {
        var email = req.Message.Trim();
        if (!email.Contains('@') || !email.Contains('.'))
        {
            return new BotMessageResponse
            {
                Reply = "That doesn't look like an email address. Could you try again?",
                State = req.State,
            };
        }
        req.State.Email = email;

        var candidates = await SearchPoliciesByEmailAsync(email, ct);
        if (candidates.Count == 0)
        {
            return new BotMessageResponse
            {
                Reply = "I couldn't find any policy under that email. Could you double-check the address?",
                State = With(req.State, BotStep.AskEmail),
            };
        }

        req.State.Candidates = candidates;
        return new BotMessageResponse
        {
            Reply = "Thanks. To make sure it's really you, could you share your date of birth (e.g. 14.03.1985) or your passport / ID number?",
            State = With(req.State, BotStep.AskDob),
        };
    }

    private async Task<BotMessageResponse> HandleDobAsync(BotMessageRequest req, CancellationToken ct)
    {
        // Accept either a date or a passport / national ID number — match
        // against whichever the customer typed.
        var input = req.Message.Trim();
        var parsedDob = ParseDob(input);
        var identityProbe = NormaliseIdentity(input);

        var match = parsedDob is not null
            ? req.State.Candidates.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.DateOfBirth) && c.DateOfBirth == parsedDob)
            : null;

        if (match is null && !string.IsNullOrEmpty(identityProbe))
        {
            match = req.State.Candidates.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.IdentityNumber)
                && string.Equals(NormaliseIdentity(c.IdentityNumber!), identityProbe, StringComparison.OrdinalIgnoreCase));
        }

        if (parsedDob is null && string.IsNullOrEmpty(identityProbe))
        {
            return new BotMessageResponse
            {
                Reply = "I couldn't read that as a date or a passport / ID number. Try DD.MM.YYYY (e.g. 14.03.1985) or your document number.",
                State = req.State,
            };
        }

        if (match is null)
        {
            req.State.VerifyAttempts++;
            if (req.State.VerifyAttempts >= MaxVerifyAttempts)
            {
                req.State.Step = BotStep.Blocked;
                logger.LogWarning("[bot] verification blocked for email={Email} after {Attempts} attempts",
                    req.State.Email, req.State.VerifyAttempts);
                return new BotMessageResponse
                {
                    Reply = "I'm not able to verify your identity from the information given. Please contact support for help.",
                    State = req.State,
                };
            }
            return new BotMessageResponse
            {
                Reply = $"That doesn't match our records ({MaxVerifyAttempts - req.State.VerifyAttempts} attempts left). Could you try again?",
                State = req.State,
            };
        }

        // Narrow candidate list to just the matched person and their policies.
        req.State.SelectedPersonId = match.PersonId;
        var policies = req.State.Candidates.Where(c => c.PersonId == match.PersonId).ToList();
        req.State.Candidates = policies;

        if (policies.Count == 1)
        {
            req.State.SelectedPolicyId = policies[0].PolicyId;
            return await AfterPolicySelectedAsync(req.State, policies[0], ct);
        }

        return new BotMessageResponse
        {
            Reply = "Thanks, verified. You have several policies — which one is this claim for?",
            Options = policies.Select(p => $"{p.DisplayNumber} – {p.ProductName}").ToList(),
            State = With(req.State, BotStep.SelectPolicy),
        };
    }

    private async Task<BotMessageResponse> HandleSelectPolicyAsync(BotMessageRequest req, CancellationToken ct)
    {
        var text = req.Message.Trim();
        // Match by display number prefix (the option labels start with that).
        var picked = req.State.Candidates.FirstOrDefault(p =>
            text.StartsWith(p.DisplayNumber, StringComparison.OrdinalIgnoreCase) ||
            text.Contains(p.DisplayNumber, StringComparison.OrdinalIgnoreCase));
        if (picked is null)
        {
            return new BotMessageResponse
            {
                Reply = "I didn't catch which policy you meant. Could you pick one of the options below?",
                Options = req.State.Candidates.Select(p => $"{p.DisplayNumber} – {p.ProductName}").ToList(),
                State = req.State,
            };
        }
        req.State.SelectedPolicyId = picked.PolicyId;
        return await AfterPolicySelectedAsync(req.State, picked, ct);
    }

    /// <summary>
    /// Fetch the insured list for the selected policy via NIS and either
    /// auto-pick (single traveler) or ask the user who is claiming.
    /// </summary>
    private async Task<BotMessageResponse> AfterPolicySelectedAsync(
        BotState state, BotPolicyCandidate policy, CancellationToken ct)
    {
        var insured = await FetchInsuredAsync(policy.PolicyId, ct);
        state.Insured = insured;

        if (insured.Count == 1)
        {
            ApplyInsured(state, insured[0]);
            return new BotMessageResponse
            {
                Reply = $"Thanks. Your claim will be on policy **{policy.DisplayNumber}** ({policy.ProductName}) for **{insured[0].Name}**. If you have any related documents (bills, receipts, photos), please upload them now — type 'done' when finished.",
                State = With(state, BotStep.AskDocuments),
                AwaitingUpload = true,
            };
        }

        if (insured.Count == 0)
        {
            // Shouldn't normally happen, but fall back to using the verified
            // person rather than asking an impossible question.
            var fallback = policy.HolderName;
            state.SelectedInsuredName = fallback;
            state.SelectedInsuredDob = policy.DateOfBirth;
            return new BotMessageResponse
            {
                Reply = $"Got policy **{policy.DisplayNumber}**. If you have any related documents, upload them now — type 'done' when finished.",
                State = With(state, BotStep.AskDocuments),
                AwaitingUpload = true,
            };
        }

        return new BotMessageResponse
        {
            Reply = $"There are several travelers on policy **{policy.DisplayNumber}**. Who is this claim for?",
            Options = insured.Select(i => i.Name).ToList(),
            State = With(state, BotStep.SelectInsured),
        };
    }

    private BotMessageResponse HandleSelectInsured(BotMessageRequest req)
    {
        var text = req.Message.Trim();
        // Match by full or partial name; fall back to first-name prefix.
        var picked = req.State.Insured.FirstOrDefault(i =>
                string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? req.State.Insured.FirstOrDefault(i =>
                i.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        if (picked is null)
        {
            return new BotMessageResponse
            {
                Reply = "I didn't catch which person you meant. Could you pick one of the options below?",
                Options = req.State.Insured.Select(i => i.Name).ToList(),
                State = req.State,
            };
        }
        ApplyInsured(req.State, picked);
        return new BotMessageResponse
        {
            Reply = $"Got it — this claim is for **{picked.Name}**. If you have any related documents, upload them now — type 'done' when finished.",
            State = With(req.State, BotStep.AskDocuments),
            AwaitingUpload = true,
        };
    }

    private static void ApplyInsured(BotState state, BotInsuredCandidate insured)
    {
        state.SelectedInsuredUid = insured.Uid;
        state.SelectedInsuredName = insured.Name;
        state.SelectedInsuredDob = insured.DateOfBirth;
    }

    private BotMessageResponse HandleAskDocuments(BotMessageRequest req)
    {
        var msg = req.Message.Trim().ToLowerInvariant();
        if (msg is "done" or "skip" or "no" or "nothing")
        {
            return BuildConfirm(req.State);
        }
        // If the client sends a message that isn't 'done', keep them in upload mode.
        return new BotMessageResponse
        {
            Reply = "Got it. Add more files if you have them, or type 'done' to continue.",
            State = req.State,
            AwaitingUpload = true,
        };
    }

    private BotMessageResponse HandleConfirm(BotMessageRequest req)
    {
        var msg = req.Message.Trim().ToLowerInvariant();
        if (msg is "submit" or "yes" or "y" or "confirm")
        {
            // Actual submission happens in BotController via the dedicated endpoint;
            // here we just nudge the client to call /submit.
            return new BotMessageResponse
            {
                Reply = "Submitting your claim…",
                State = With(req.State, BotStep.Submitting),
            };
        }
        if (msg is "cancel" or "no" or "n")
        {
            return new BotMessageResponse
            {
                Reply = "OK, I won't submit. You can start over by refreshing.",
                State = req.State,
            };
        }
        return BuildConfirm(req.State);
    }

    // ---- Claim submission --------------------------------------------------

    public async Task<BotMessageResponse> SubmitAsync(BotState state, IFormFileCollection files, CancellationToken ct)
    {
        if (state.SelectedPolicyId is null)
        {
            return new BotMessageResponse
            {
                Reply = "Something's missing. Let's start over by refreshing.",
                State = state,
            };
        }

        var shortCode = shortCodes.Generate();
        var picked = state.Candidates.First(c => c.PolicyId == state.SelectedPolicyId);

        // Build a /claims/upload body — same shape as the email-ingest path so
        // AC parses out the Claimant / Incident sections. Use the picked
        // traveler (insured) as the claimant when one was chosen; fall back
        // to the email-owner if not (single-insured policies, etc.).
        var claimantName = state.SelectedInsuredName ?? picked.HolderName;
        var claimantDob = state.SelectedInsuredDob ?? picked.DateOfBirth;
        var sb = new StringBuilder();
        sb.AppendLine($"Email: {state.Email}");
        sb.AppendLine();
        sb.AppendLine("Claimant");
        sb.AppendLine($"Name: {claimantName}");
        sb.AppendLine($"Date of birth: {claimantDob}");
        sb.AppendLine($"Policy: {picked.DisplayNumber} ({picked.ProductName})");
        sb.AppendLine();
        sb.AppendLine("Incident");
        sb.AppendLine($"What happened: {state.Description}");
        sb.AppendLine($"When: {state.IncidentDate}");
        sb.AppendLine($"Where: {state.Location}");

        var claimsClient = httpClientFactory.CreateClient("ClaimsApi");
        using var uploadContent = new System.Net.Http.MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var uploadFileContent = new System.Net.Http.ByteArrayContent(fileBytes);
        uploadFileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        uploadContent.Add(uploadFileContent, "file", "claim.txt");

        var response = await claimsClient.PostAsync(
            $"claims/upload?externalId={Uri.EscapeDataString(shortCode)}", uploadContent, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("[bot] AC POST /claims/upload failed status={Status} body={Body}",
                (int)response.StatusCode, err);
            return new BotMessageResponse
            {
                Reply = "Something went wrong creating your claim. Please try again later.",
                State = state,
            };
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var claimNode = root.TryGetProperty("claim", out var c) ? c : root;
        var claimId = claimNode.GetProperty("id").GetString()!;
        // The Claim record itself is owned by ClaimsOps and hydrated from
        // AC's reviewed webhook; nothing to persist locally.

        // /claims/upload silently drops externalId and productCode when passed
        // as query params — we have to PUT them back onto the claim afterwards
        // so AC's downstream (webhook payload + ClaimsOps inbox) can display
        // our human-readable short code and the demo product.
        await SetClaimFieldsAsync(claimsClient, claimId, shortCode, "salsin_v3", ct);

        // Upload attachments.
        foreach (var file in files)
        {
            try
            {
                using var multipart = new System.Net.Http.MultipartFormDataContent();
                using var stream = file.OpenReadStream();
                var docContent = new System.Net.Http.StreamContent(stream);
                docContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                multipart.Add(docContent, "file", file.FileName);
                var docResp = await claimsClient.PostAsync($"claims/{claimId}/documents", multipart, ct);
                if (!docResp.IsSuccessStatusCode)
                {
                    logger.LogError("[bot] document upload {Name} failed status={Status}",
                        file.FileName, (int)docResp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[bot] document upload {Name} threw", file.FileName);
            }
        }

        state.ClaimId = claimId;
        state.ShortCode = shortCode;
        state.Step = BotStep.Done;

        // Mirror the email flow — send a confirmation email so the customer
        // has the reference in writing and a link back to their claim page.
        if (!string.IsNullOrWhiteSpace(state.Email))
        {
            try
            {
                var ctx = httpContext.HttpContext;
                var baseUrl = config["App:BaseUrl"]
                    ?? (ctx is null ? "" : $"{ctx.Request.Scheme}://{ctx.Request.Host}");
                var claimUrl = $"{baseUrl.TrimEnd('/')}/claim-submitted/{claimId}";
                var firstName = state.SelectedInsuredName?.Split(' ').FirstOrDefault()
                    ?? picked.HolderName.Split(' ').FirstOrDefault()
                    ?? "";
                await emailSender.SendClaimReceivedAsync(state.Email, firstName, shortCode, claimUrl, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[bot] confirmation email failed for {Email}", state.Email);
            }
        }

        return new BotMessageResponse
        {
            Reply = $"All done! Your claim is being reviewed. Your reference is **{shortCode}**. We'll be in touch as soon as we have an update.",
            State = state,
            ShortCode = shortCode,
        };
    }

    // ---- Helpers -----------------------------------------------------------

    private static BotState With(BotState s, BotStep step)
    {
        s.Step = step;
        return s;
    }

    private static BotMessageResponse Echo(BotMessageRequest req, string reply) =>
        new() { Reply = reply, State = req.State };

    private static BotMessageResponse BuildConfirm(BotState s)
    {
        var picked = s.Candidates.FirstOrDefault(c => c.PolicyId == s.SelectedPolicyId);
        var summary = new StringBuilder();
        summary.AppendLine("Here's what I have:");
        summary.AppendLine($"• **What:** {s.Description}");
        summary.AppendLine($"• **When:** {s.IncidentDate}");
        summary.AppendLine($"• **Where:** {s.Location}");
        if (picked is not null)
            summary.AppendLine($"• **Policy:** {picked.DisplayNumber} ({picked.ProductName})");
        if (!string.IsNullOrEmpty(s.SelectedInsuredName))
            summary.AppendLine($"• **Claimant:** {s.SelectedInsuredName}");
        summary.Append("Submit?");
        return new BotMessageResponse
        {
            Reply = summary.ToString(),
            Options = new() { "Submit", "Cancel" },
            State = With(s, BotStep.Confirm),
        };
    }

    /// <summary>
    /// Strips spaces, hyphens and dots from a passport / ID number so we can
    /// match values entered in different formats against the stored one.
    /// Returns null when the input doesn't look like an identity number
    /// (too short, or all digits in a date-shaped pattern).
    /// </summary>
    private static string? NormaliseIdentity(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var stripped = new string(raw.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (stripped.Length < 4) return null;
        return stripped;
    }

    private static string? ParseDob(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var formats = new[]
        {
            "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy",
            "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "MM/dd/yyyy", "M/d/yyyy",
        };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(raw.Trim(), f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("yyyy-MM-dd");
        }
        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd))
            return dd.ToString("yyyy-MM-dd");
        return null;
    }

    /// <summary>
    /// Patch fields AC dropped during /claims/upload. Both externalId and
    /// productCode go together in a single PUT to save an HTTP round-trip.
    /// </summary>
    private async Task SetClaimFieldsAsync(HttpClient claimsClient, string claimId, string externalId, string productCode, CancellationToken ct)
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                externalId = externalId,
                productCode = productCode,
            });
            var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var res = await claimsClient.PutAsync($"claims/{claimId}", body, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[bot] PUT externalId+product on claim {Claim} failed {Status}: {Body}",
                    claimId, (int)res.StatusCode, err);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[bot] PUT externalId+product on claim {Claim} threw", claimId);
        }
    }

    private async Task<List<BotInsuredCandidate>> FetchInsuredAsync(Guid policyId, CancellationToken ct)
    {
        var list = new List<BotInsuredCandidate>();
        var policy = await policySystem.GetByIdAsync(policyId, ct);
        if (policy is null) return list;
        foreach (var i in policy.Insureds)
        {
            list.Add(new BotInsuredCandidate(
                Uid: i.Id.ToString(),
                Name: $"{i.FirstName} {i.LastName}".Trim(),
                DateOfBirth: i.DateOfBirth,
                Email: i.Email));
        }
        return list;
    }

    private async Task<List<BotPolicyCandidate>> SearchPoliciesByEmailAsync(string email, CancellationToken ct)
    {
        var list = new List<BotPolicyCandidate>();
        var policies = await policySystem.FindByEmailAsync(email, ct);
        foreach (var p in policies)
        {
            if (string.Equals(p.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) continue;
            // The bot's flow is keyed on the person who started it — match
            // by email so we get DOB and identity from the right insured.
            var matchingInsured = p.Insureds.FirstOrDefault(i =>
                string.Equals(i.Email, email, StringComparison.OrdinalIgnoreCase));
            if (matchingInsured is null) continue;
            var holderName = p.Holder is null
                ? matchingInsured.FirstName + " " + matchingInsured.LastName
                : $"{p.Holder.FirstName} {p.Holder.LastName}";
            list.Add(new BotPolicyCandidate(
                PolicyId: p.Id,
                DisplayNumber: p.DisplayNumber,
                ProductName: p.ProductName,
                HolderName: holderName.Trim(),
                PersonId: matchingInsured.Id.ToString(),
                DateOfBirth: matchingInsured.DateOfBirth,
                IdentityNumber: matchingInsured.IdentityNumber));
        }
        return list;
    }
}
