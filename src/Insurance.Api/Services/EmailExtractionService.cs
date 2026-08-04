using System.Text;
using System.Text.Json;

namespace Insurance.Api.Services;

public record EmailExtraction(
    string? FirstName,
    string? LastName,
    string? PolicyNumber,
    string? IncidentDate,
    string? IncidentDescription,
    string? StreetAddress,
    string? City,
    string? PostalCode,
    string? CountryCode,
    string? PhoneNumber
);

public class EmailExtractionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<EmailExtractionService> logger)
{
    public async Task<EmailExtraction> ExtractAsync(string subject, string body, CancellationToken ct)
    {
        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("[email-extract] Anthropic:ApiKey not configured — returning empty extraction");
            return new EmailExtraction(null, null, null, null, null, null, null, null, null, null);
        }

        var prompt = $$"""
            Extract information from this insurance claim email. Return only valid JSON with these fields (use null for anything not found):
            {
              "firstName": string,
              "lastName": string,
              "policyNumber": string,
              "incidentDate": "YYYY-MM-DD",
              "incidentDescription": string,
              "streetAddress": string,
              "city": string,
              "postalCode": string,
              "countryCode": "NL",
              "phoneNumber": string
            }

            Subject: {{subject}}
            Body: {{body}}
            """;

        var requestBody = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 500,
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
            var errBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("[email-extract] Anthropic API failed status={Status} body={Body}",
                (int)response.StatusCode, errBody);
            return new EmailExtraction(null, null, null, null, null, null, null, null, null, null);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        // Strip markdown code fences if present
        text = text.Trim();
        if (text.StartsWith("```")) text = text[(text.IndexOf('\n') + 1)..];
        if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")].TrimEnd();

        try
        {
            using var result = JsonDocument.Parse(text);
            var r = result.RootElement;
            return new EmailExtraction(
                FirstName: r.TryGetString("firstName"),
                LastName: r.TryGetString("lastName"),
                PolicyNumber: r.TryGetString("policyNumber"),
                IncidentDate: r.TryGetString("incidentDate"),
                IncidentDescription: r.TryGetString("incidentDescription"),
                StreetAddress: r.TryGetString("streetAddress"),
                City: r.TryGetString("city"),
                PostalCode: r.TryGetString("postalCode"),
                CountryCode: r.TryGetString("countryCode") ?? "NL",
                PhoneNumber: r.TryGetString("phoneNumber")
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[email-extract] failed to parse Claude JSON response: {Text}", text);
            return new EmailExtraction(null, null, null, null, null, null, null, null, "NL", null);
        }
    }
}

file static class JsonElementExtensions
{
    public static string? TryGetString(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
