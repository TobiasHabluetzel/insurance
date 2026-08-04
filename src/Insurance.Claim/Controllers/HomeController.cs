using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Insurance.Claim.Models;
using Insurance.Claim.Services;

namespace Insurance.Claim.Controllers;

public class HomeController(
    ILogger<HomeController> logger,
    PolicyApiClient policyApi,
    IConfiguration config) : Controller
{
    /// <summary>
    /// Entry page — the MVC port of the React PolicyEntryPage. GET renders
    /// an empty form; POST looks up the policy on Insurance.Api server-side
    /// and redirects to the dashboard when it resolves.
    /// </summary>
    [HttpGet("/")]
    public Task<IActionResult> Index([FromQuery] string? policy, CancellationToken ct)
    {
        // Deep-link support: /?policy=P-2026-001067 auto-submits the form
        // (the React version did this from a useEffect). We do the same
        // server-side by running the same lookup as the POST handler.
        if (!string.IsNullOrWhiteSpace(policy))
        {
            return LookupCore(new PolicyEntryViewModel { Number = policy.Trim() }, ct);
        }
        return Task.FromResult<IActionResult>(View(new PolicyEntryViewModel()));
    }

    [HttpPost("/")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Lookup(PolicyEntryViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return Task.FromResult<IActionResult>(View(nameof(Index), model));
        return LookupCore(model, ct);
    }

    private async Task<IActionResult> LookupCore(PolicyEntryViewModel model, CancellationToken ct)
    {
        try
        {
            var found = await policyApi.SearchByNumberAsync(model.Number!.Trim(), ct);
            if (found is null)
            {
                model.Error = "Policy not found";
                return View(nameof(Index), model);
            }
            return RedirectToAction(nameof(Dashboard), new { policyId = found.PolicyId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy lookup failed for {Number}", model.Number);
            model.Error = "Something went wrong. Please try again.";
            return View(nameof(Index), model);
        }
    }

    [HttpGet("/dashboard/{policyId}")]
    public async Task<IActionResult> Dashboard(string policyId, CancellationToken ct)
    {
        var dashboard = await policyApi.GetDashboardAsync(policyId, ct);
        if (dashboard is null) return NotFound();

        var vm = new DashboardViewModel
        {
            PolicyId = dashboard["policyId"]?.GetValue<string>() ?? policyId,
            PolicyNumber = dashboard["policyNumber"]?.GetValue<string>() ?? "",
            HolderFirstName = dashboard["holderFirstName"]?.GetValue<string>() ?? "",
            HolderName = dashboard["holderName"]?.GetValue<string>() ?? "",
            ProductName = dashboard["productName"]?.GetValue<string>() ?? "",
            ClaimsCount = dashboard["claimsCount"]?.GetValue<int>() ?? 0,
            RawPolicy = dashboard,
        };
        return View(vm);
    }

    /// <summary>
    /// FileClaim shell — the Razor page renders the layout server-side and
    /// hands the raw policy JSON to the AngularJS FileClaimController via
    /// window.__initialPolicy. Interactive form pieces (dropdown/date
    /// picker/etc.) are AngularJS directives inside the view.
    /// </summary>
    [HttpGet("/file-claim/{policyId}")]
    public async Task<IActionResult> FileClaim(string policyId, CancellationToken ct)
    {
        var dashboard = await policyApi.GetDashboardAsync(policyId, ct);
        if (dashboard is null) return NotFound();
        // Google Maps key for the address picker — server-side config only.
        ViewData["GoogleMapsApiKey"] = config["Google:MapsApiKey"] ?? "";
        return View(dashboard);
    }

    /// <summary>
    /// Post-submit "processing" page. Everything interactive (SSE stream,
    /// progress ring, additions form, follow-up flow) runs client-side
    /// inside ClaimSubmittedController — this action just hands the claim
    /// id through to the view.
    /// </summary>
    [HttpGet("/claim-submitted/{claimId}")]
    public IActionResult ClaimSubmitted(string claimId) => View(model: claimId);

    /// <summary>
    /// Standalone evidence-upload page reached from the "action needed"
    /// email link. Same route shape as the React app.
    /// </summary>
    [HttpGet("/claim/{claimId}/evidence")]
    public IActionResult ClaimEvidence(string claimId) => View(model: claimId);

    /// <summary>
    /// Chat widget page — embedded in the portal iframe. Runs its own
    /// standalone layout (no shared _Layout) so it stays visually clean
    /// inside the widget.
    /// </summary>
    [HttpGet("/bot")]
    public IActionResult Bot() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
