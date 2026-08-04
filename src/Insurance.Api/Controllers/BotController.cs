using Microsoft.AspNetCore.Mvc;
using Insurance.Api.Models;
using Insurance.Api.Services;

namespace Insurance.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BotController(BotConversationService bot) : ControllerBase
{
    /// <summary>Returns the initial bot greeting to seed a fresh conversation.</summary>
    [HttpGet("welcome")]
    public ActionResult<BotMessageResponse> Welcome() => bot.Welcome();

    /// <summary>Advances the conversation by one turn.</summary>
    [HttpPost("message")]
    public async Task<ActionResult<BotMessageResponse>> Message(
        [FromBody] BotMessageRequest request,
        CancellationToken ct)
    {
        if (request is null || request.State is null)
            return BadRequest("state required");
        return await bot.HandleMessageAsync(request, ct);
    }

    /// <summary>
    /// Final claim submission. Multipart so the bot can attach the files the
    /// user uploaded during the conversation. The state JSON is sent as a
    /// `state` form field.
    /// </summary>
    [HttpPost("submit")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<BotMessageResponse>> Submit(
        [FromForm] string state,
        [FromForm] IFormFileCollection? files,
        CancellationToken ct)
    {
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<BotState>(state, opts);
        if (deserialized is null) return BadRequest("invalid state");
        return await bot.SubmitAsync(deserialized, files ?? new FormFileCollection(), ct);
    }
}
