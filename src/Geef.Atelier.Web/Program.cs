using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Embeddings;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Infrastructure.Knowledge;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Mcp;
using Geef.Atelier.Web.Auth;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Endpoints;
using Geef.Atelier.Web.Hubs;
using Geef.Atelier.Web.Notifications;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAI-compatible LLM client (default: OpenRouter) — set Llm__ApiKey env-var for real calls.
builder.Services.AddLlmClient(builder.Configuration)
    .AddStandardResilienceHandler();

builder.Services.AddDbContext<AtelierDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAtelierPersistence();
builder.Services.AddAtelierApplication();

builder.Services.AddEmbeddings(builder.Configuration);
builder.Services.AddKnowledge(builder.Configuration);

// Grounding providers — ApiKey intentionally not logged; missing key fails at run time, not at startup.
builder.Services.AddGroundingProviders(builder.Configuration);

builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<CostTrackingOptions>(builder.Configuration.GetSection("CostTracking"));
builder.Services.AddSingleton<IPricingCatalog, PricingCatalog>();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection("Orchestrator"));
builder.Services.Configure<ConvergenceOptions>(builder.Configuration.GetSection("Convergence"));
builder.Services.AddHostedService<RunOrchestratorService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtelierDbContext>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();

builder.Services.AddAtelierAuth(builder.Configuration);
builder.Services.AddAtelierMcpAuth(builder.Configuration);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath        = "/login";
        o.LogoutPath       = "/auth/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan   = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly  = true;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Name     = "Atelier.Auth";
    })
    .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>(
        McpAuthorizationConstants.BearerScheme, _ => { });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(McpAuthorizationConstants.McpPolicy, p =>
    {
        p.AuthenticationSchemes = new[] { McpAuthorizationConstants.BearerScheme };
        p.RequireAuthenticatedUser();
    });
});

builder.Services.AddAtelierMcp();
builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Auto-migrate at startup; failure is logged but does not crash the host
// so that the health check can report Unhealthy rather than causing an unhandled exception.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await scope.ServiceProvider
            .GetRequiredService<AtelierDbContext>()
            .Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed on startup; health check will report Unhealthy");
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Prevent caching of authenticated responses.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
        ctx.Response.Headers.CacheControl = "no-store, no-cache";
    await next();
});

app.MapHealthChecks("/health").AllowAnonymous();

app.MapHub<RunHub>("/hubs/runs");

app.MapMcp("/mcp").RequireAuthorization(McpAuthorizationConstants.McpPolicy);

app.MapAuthEndpoints();
app.MapSettingsEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
