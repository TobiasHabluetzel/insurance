using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Insurance.Api.Data;
using Insurance.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (portal Postgres) --------------------------------------------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---- ASP.NET Identity (portal auth) ----------------------------------------
// UserName = EmailHash. Passwords stay hashed by Identity's default hasher.
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(o =>
{
    o.User.RequireUniqueEmail = false;
    o.SignIn.RequireConfirmedEmail = false;
    o.Password.RequiredLength = 8;
    o.Password.RequireDigit = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ---- JWT bearer for portal sessions ----------------------------------------
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required");
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero,
    };
});

// External OAuth providers — registered only when configured so the app
// boots without them in dev.
var authBuilder = builder.Services.AddAuthentication();
if (!string.IsNullOrEmpty(builder.Configuration["Auth:Google:ClientId"]))
    authBuilder.AddGoogle(o =>
    {
        o.ClientId = builder.Configuration["Auth:Google:ClientId"]!;
        o.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"]!;
        o.SignInScheme = IdentityConstants.ExternalScheme;
    });
if (!string.IsNullOrEmpty(builder.Configuration["Auth:Microsoft:ClientId"]))
    authBuilder.AddMicrosoftAccount(o =>
    {
        o.ClientId = builder.Configuration["Auth:Microsoft:ClientId"]!;
        o.ClientSecret = builder.Configuration["Auth:Microsoft:ClientSecret"]!;
        o.SignInScheme = IdentityConstants.ExternalScheme;
    });

builder.Services.AddAuthorization();

// ---- Controllers + JSON --------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();

// ---- Portal services -----------------------------------------------------
builder.Services.AddScoped<EmailHashService>();
builder.Services.AddScoped<PortalPolicyService>();
builder.Services.AddScoped<TokenService>();

// ---- Claim + bot + email services ----------------------------------------
builder.Services.AddSingleton<ClaimEventService>();
builder.Services.AddScoped<ClaimClassificationService>();
builder.Services.AddScoped<EmailExtractionService>();
builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<ShortCodeService>();
builder.Services.AddScoped<BotExtractionService>();
builder.Services.AddScoped<BotConversationService>();
builder.Services.AddScoped<PolicySystemService>();

// ---- Outbound HTTP clients -----------------------------------------------
builder.Services.AddHttpClient("Resend", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", builder.Configuration["Resend:ApiKey"]);
});

builder.Services.AddHttpClient("ClaimsApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ClaimsApi:Endpoint"]!);
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", builder.Configuration["ClaimsApi:ApiToken"]);
});

// Policy demo system — shared by both the portal (insured lookup) and the
// claim wizard (policy search). One HttpClient, two typed services on top.
builder.Services.AddHttpClient("PolicySystem", client =>
{
    var endpoint = builder.Configuration["PolicySystem:Endpoint"]
        ?? throw new InvalidOperationException("PolicySystem:Endpoint is required");
    client.BaseAddress = new Uri(endpoint);
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/healthz", () => Results.Ok("healthy"));

// Apply any pending EF migrations on startup so the portal's identity
// tables are ready before the first request.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

app.Run();
