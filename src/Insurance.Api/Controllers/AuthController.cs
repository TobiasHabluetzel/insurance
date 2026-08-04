using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Insurance.Api.Data;
using Insurance.Api.Services;

namespace Insurance.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    EmailHashService emailHash,
    PortalPolicyService policySystem,
    TokenService tokens) : ControllerBase
{
    // POST /api/auth/check-email
    // Determines whether the email is known locally; otherwise asks the policy
    // demo system whether anyone with that email exists. Email itself is NEVER
    // persisted — only the computed hash.
    [HttpPost("check-email")]
    public async Task<IActionResult> CheckEmail([FromBody] CheckEmailRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest("Email is required");

        var hash = emailHash.Compute(req.Email);
        var user = await userManager.FindByNameAsync(hash);

        if (user is not null)
        {
            var providers = await userManager.GetLoginsAsync(user);
            return Ok(new
            {
                status = "known",
                emailHash = hash,
                hasPassword = await userManager.HasPasswordAsync(user),
                externalProviders = providers.Select(p => p.LoginProvider).ToArray(),
            });
        }

        // Not in local DB — ask the policy system.
        var insured = await policySystem.FindByEmailAsync(req.Email, ct);
        return Ok(new
        {
            status = "unknown",
            emailHash = hash,
            // "single" if we found a match in the policy system, "none" otherwise.
            // Email is unique in the new model so disambiguation never happens.
            policyMatch = insured is null ? "none" : "single",
            insuredId = insured?.Id,
        });
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmailHash) || req.InsuredId == Guid.Empty)
            return BadRequest("emailHash and insuredId are required");

        var existing = await userManager.FindByNameAsync(req.EmailHash);
        if (existing is not null)
            return Ok(new { token = tokens.Generate(existing) });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = req.EmailHash,
            EmailHash = req.EmailHash,
            InsuredId = req.InsuredId,
        };

        IdentityResult result;
        if (!string.IsNullOrEmpty(req.Password))
            result = await userManager.CreateAsync(user, req.Password);
        else
            result = await userManager.CreateAsync(user);

        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok(new { token = tokens.Generate(user) });
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmailHash) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("emailHash and password are required");

        var user = await userManager.FindByNameAsync(req.EmailHash);
        if (user is null) return Unauthorized();

        var ok = await userManager.CheckPasswordAsync(user, req.Password);
        if (!ok) return Unauthorized();

        return Ok(new { token = tokens.Generate(user) });
    }
}

public record CheckEmailRequest(string Email);
public record RegisterRequest(string EmailHash, Guid InsuredId, string? Password);
public record LoginRequest(string EmailHash, string Password);
