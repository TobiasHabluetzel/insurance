using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Insurance.Portal.Models;
using Insurance.Portal.Services;

namespace Insurance.Portal.Controllers;

/// <summary>
/// The portal's shell is tiny — just two pages (login and dashboard).
/// Each action renders a Razor view; the AngularJS controllers on the
/// client do everything else (auth token lifecycle, /api/portal fetches,
/// chat widget, etc.). Auth is Bearer-token so we deliberately do NOT
/// use ASP.NET auth middleware here — the AngularJS DashboardController
/// bounces to "/" if no token is stored.
/// </summary>
public class HomeController(PortalConfig portalConfig) : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View();

    [HttpGet("/dashboard")]
    public IActionResult Dashboard()
    {
        ViewData["ClaimAppUrl"] = portalConfig.ClaimAppUrl;
        ViewData["BotUrl"] = portalConfig.BotUrl;
        ViewData["ClaimEmail"] = portalConfig.ClaimEmail;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
