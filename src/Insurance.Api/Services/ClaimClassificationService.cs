using System.Text;
using System.Text.Json;

namespace Insurance.Api.Services;

public class ClaimClassificationService(IHttpClientFactory httpClientFactory, IConfiguration config)
{
    private static readonly string[] ValidCategories = ["accident", "medical", "travel", "theft", "property", "other"];

    public async Task<string> ClassifyAsync(string incidentDescription, CancellationToken ct)
    {
        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey)) return "other";

        var prompt = $"""
            Classify this insurance claim incident into exactly one of these categories:
            accident, medical, travel, theft, property, other

            Incident description: {incidentDescription}

            Reply with only the single category word, nothing else.
            """;

        var body = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 10,
            temperature = 0,
            messages = new[] { new { role = "user", content = prompt } }
        });

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return "other";

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()
            ?.Trim().ToLower() ?? "other";

        return ValidCategories.Contains(text) ? text : "other";
    }
}
