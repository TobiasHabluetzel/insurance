using System.Net.Http.Headers;
using Insurance.Claim.Services;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Typed HttpClient hitting the Insurance.Api backend. Configured via
// Api:Endpoint (env var Api__Endpoint on Railway). Controllers call this
// server-side so the browser never sees the API base URL directly.
builder.Services.AddHttpClient("Api", client =>
{
    var endpoint = builder.Configuration["Api:Endpoint"]
        ?? throw new InvalidOperationException(
            "Api:Endpoint is required — set Api__Endpoint to the Insurance.Api base URL.");
    client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped<PolicyApiClient>();

// Brand theme is picked once at startup from the Design env var. Mirrors
// what VITE_DESIGN used to do in the React project. Falls back to degoudse.
builder.Services.AddSingleton(sp => new BrandTheme(
    builder.Configuration["Design"] ?? "degoudse"));

// Demo-only switches, read once at startup like the brand theme so they cannot
// be flipped by a query string. Off by default — see DemoOptions.
builder.Services.AddSingleton(sp => new DemoOptions(
    builder.Configuration.GetValue("Demo:ManagementView", false)));

// Reverse proxy: /api/* → the Insurance.Api service. Configured off the
// same Api:Endpoint env var used by PolicyApiClient so there's a single
// source of truth. The browser still calls "/api/..." on same origin —
// YARP forwards it. Multipart uploads, SSE streams, and the
// /claims/{id}/documents downloads all pass through unchanged.
{
    var apiEndpoint = builder.Configuration["Api:Endpoint"]
        ?? throw new InvalidOperationException(
            "Set Api__Endpoint (double underscore) to the Insurance.Api base URL, e.g. https://goudse-production.up.railway.app");
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

// Reverse proxy sits BEFORE the MVC route table so /api/* never touches
// controller resolution.
app.MapReverseProxy();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Health probe — mirrors Insurance.Api so Railway can point at either service.
app.MapGet("/healthz", () => Results.Ok("healthy"));

app.Run();
