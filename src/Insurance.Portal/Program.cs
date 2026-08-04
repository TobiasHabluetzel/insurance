using Insurance.Portal.Services;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Brand theme (VITE_DESIGN equivalent). Falls back to degoudse.
builder.Services.AddSingleton(sp => new BrandTheme(
    builder.Configuration["Design"] ?? "degoudse"));

// Injected into views/controllers as read-only config. The AngularJS
// dashboard controller uses these to build the "File a claim" deep link
// and the "Claim by mail" mailto: URI.
builder.Services.AddSingleton(new PortalConfig(
    ClaimAppUrl: builder.Configuration["ClaimApp:Url"] ?? "https://theinsuranceclaim-production.up.railway.app",
    BotUrl: builder.Configuration["Bot:Url"] ?? "https://theinsuranceclaim-production.up.railway.app/bot",
    ClaimEmail: builder.Configuration["ClaimEmail"] ?? "8347929027ad07a7372a@cloudmailin.net"));

// Reverse proxy: /api/* → Insurance.Api. The browser sees a
// single origin, which keeps the OAuth challenge redirect flow simple
// (no CORS on state cookies) and lets the JWT stay in localStorage.
{
    var apiEndpoint = builder.Configuration["Api:Endpoint"]
        ?? throw new InvalidOperationException(
            "Set Api__Endpoint (double underscore) to the Insurance.Api base URL, e.g. https://theinsuranceportal-production.up.railway.app");
    builder.Services.AddReverseProxy().LoadFromMemory(
        new[]
        {
            new RouteConfig
            {
                RouteId = "api",
                ClusterId = "api-cluster",
                Match = new RouteMatch { Path = "/api/{**catch-all}" },
            },
        },
        new[]
        {
            new ClusterConfig
            {
                ClusterId = "api-cluster",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["primary"] = new DestinationConfig { Address = apiEndpoint },
                },
            },
        });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Reverse proxy runs before route resolution so /api/* never hits MVC.
app.MapReverseProxy();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/healthz", () => Results.Ok("healthy"));

app.Run();
