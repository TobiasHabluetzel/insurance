using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Insurance.Api.Data;
using Insurance.Api.Services;

namespace Insurance.Api.Controllers;

[ApiController]
[Route("api/portal")]
[Authorize]
public class PortalController(
    UserManager<ApplicationUser> userManager,
    PortalPolicyService policySystem) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!);

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(UserId.ToString());
        if (user?.InsuredId is null) return Unauthorized();

        var dashboard = await policySystem.GetDashboardAsync(user.InsuredId.Value, ct);
        if (dashboard is null) return NotFound();

        return Ok(new
        {
            firstName = dashboard.FirstName,
            lastName = dashboard.LastName,
            policies = dashboard.Policies
                .Where(p => p.IsHolder && !string.Equals(p.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .Select(p => new
                {
                    policyId = p.Id,
                    displayNumber = p.DisplayNumber,
                    productName = p.ProductName,
                    periodStart = p.PeriodStart,
                    periodEnd = p.PeriodEnd,
                    destination = p.Destination,
                    status = p.Status,
                    // Claims are not tracked in the demo yet; the portal only renders 0 for now.
                    claimsCount = 0,
                    travelers = p.Travelers.Select(t => new
                    {
                        firstName = t.FirstName,
                        lastName = t.LastName,
                        isHolder = t.IsHolder,
                    }),
                }).ToArray(),
        });
    }

    [HttpGet("policies/{policyId:guid}")]
    public async Task<IActionResult> GetPolicy(Guid policyId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(UserId.ToString());
        if (user?.InsuredId is null) return Unauthorized();

        var dashboard = await policySystem.GetDashboardAsync(user.InsuredId.Value, ct);
        if (dashboard is null) return NotFound();

        var match = dashboard.Policies.FirstOrDefault(p => p.Id == policyId);
        if (match is null) return NotFound();
        // Authorization: only the holder of this policy can view it.
        if (!match.IsHolder) return Forbid();

        return Ok(new
        {
            policyId = match.Id,
            policyNumber = match.DisplayNumber,
            productName = match.ProductName,
            periodStart = match.PeriodStart,
            periodEnd = match.PeriodEnd,
            status = match.Status,
        });
    }
}
