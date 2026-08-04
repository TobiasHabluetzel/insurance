using System.Text;
using System.Text.Json;

namespace Insurance.Api.Services;

public record BotExtraction(
    string? Description,
    bool DescriptionComplete,
    string? IncidentDate,
    string? Location);

public class BotExtractionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<BotExtractionService> logger)
{
    /// <summary>
    /// Looks at the conversation so far + the latest user utterance and
    /// extracts whatever incident fields are mentioned. Existing values in
    /// <paramref name="known"/> are preserved unless the new utterance
    /// supplies a clearer value (Claude decides).
    /// </summary>
    public async Task<BotExtraction> ExtractAsync(
        BotExtraction known, string userUtterance, CancellationToken ct)
    {
        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("[bot-extract] Anthropic:ApiKey not configured — passing through");
            return known;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var prompt = $$"""
            You are helping a customer file an insurance claim. You already know
            the following about the incident (some fields may be missing):

            {
              "description": {{Json(known.Description)}},
              "incidentDate": {{Json(known.IncidentDate)}},
              "location": {{Json(known.Location)}}
            }

            The customer just said (JSON-encoded): {{Json(userUtterance)}}

            Return ONLY this JSON object, no commentary:
            {
              "description": string | null,
              "descriptionComplete": boolean,
              "incidentDate": string | null,    // ISO YYYY-MM-DD
              "location": string | null
            }

            Rules:
            - "description" is a one-sentence summary of what happened. Keep the
              prior description if the new utterance only adds detail; replace
              it if the new utterance corrects what happened.
            - "descriptionComplete" is true when the description identifies WHAT
              happened concretely enough that a claims handler could start
              working on it; false when it's a vague label without specifics.
              Examples:
                "my baggage was stolen"                            -> true
                "I slipped on ice and broke my arm"                -> true
                "I had to cancel my flight because I was sick"     -> true
                "I had an accident"                                -> false (what kind?)
                "I had to cancel my flight"                        -> false (why?)
                "I got sick"                                       -> false (what symptoms or impact?)
                "something happened"                               -> false
            - "incidentDate" must be ISO YYYY-MM-DD or null. Resolve "yesterday",
              "last Friday", etc. relative to today {{today}}.
            - "location" is a short human-readable location (city + country if
              given), or null.
            - Use null for anything still unknown — do NOT make values up.
            """;

        var requestBody = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 300,
            temperature = 0,
            messages = new[] { new { role = "user", content = prompt } }
        });

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("[bot-extract] Anthropic API failed status={Status} body={Body}",
                (int)response.StatusCode, err);
            return known;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        text = text.Trim();
        if (text.StartsWith("```")) text = text[(text.IndexOf('\n') + 1)..];
        if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")].TrimEnd();

        try
        {
            using var result = JsonDocument.Parse(text);
            var r = result.RootElement;
            var complete = r.TryGetProperty("descriptionComplete", out var dc)
                && dc.ValueKind == JsonValueKind.True;
            return new BotExtraction(
                Description: r.TryGetString("description") ?? known.Description,
                DescriptionComplete: complete,
                IncidentDate: r.TryGetString("incidentDate") ?? known.IncidentDate,
                Location: r.TryGetString("location") ?? known.Location
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[bot-extract] failed to parse Claude JSON: {Text}", text);
            return known;
        }
    }

    private static string Json(string? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value);
}

file static class JsonElementExtensions
{
    public static string? TryGetString(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }
}
