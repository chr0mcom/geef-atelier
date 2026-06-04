using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Embeddings;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Infrastructure.Knowledge;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.TemplateStudio;
using Geef.Atelier.Mcp;
using Geef.Atelier.Web.Auth;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Endpoints;
using Geef.Atelier.Web.Hubs;
using Geef.Atelier.Web.Notifications;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Persist DataProtection keys to a mounted volume so auth cookies, antiforgery tokens and
// Blazor circuits survive container restarts/redeploys. Without this, every redeploy rotates
// the in-container keys, invalidating sessions and breaking active circuits mid-interaction.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/dataprotection-keys"))
    .SetApplicationName("Geef.Atelier");

// OpenAI-compatible LLM client (default: OpenRouter) — set Llm__ApiKey env-var for real calls.
builder.Services.AddLlmClient(builder.Configuration)
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout        = TimeSpan.FromMinutes(30);
        options.AttemptTimeout.Timeout             = TimeSpan.FromMinutes(28);
        options.CircuitBreaker.SamplingDuration    = TimeSpan.FromMinutes(60);
        options.Retry.MaxRetryAttempts             = 2;
    });

builder.Services.AddDbContext<AtelierDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           // Raw-SQL migrations (Step19McpOAuth+) deliberately bypass EF schema tracking;
           // the tables exist in the DB but not in the model snapshot.
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddAtelierPersistence();
builder.Services.AddAtelierApplication();

builder.Services.AddEmbeddings(builder.Configuration);
builder.Services.AddKnowledge(builder.Configuration);

// Crew composition infrastructure (ICrewTemplateEmbeddingRepository, etc.)
builder.Services.AddCrewComposition();

// Grounding providers — ApiKey intentionally not logged; missing key fails at run time, not at startup.
builder.Services.AddGroundingProviders(builder.Configuration);

builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<CostTrackingOptions>(builder.Configuration.GetSection("CostTracking"));
builder.Services.AddSingleton<IPricingCatalog, PricingCatalog>();
builder.Services.AddSingleton<DocsService>();
builder.Services.AddTemplateStudio(builder.Configuration);
builder.Services.AddFinalizers(builder.Configuration);

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection("Orchestrator"));
builder.Services.Configure<ConvergenceOptions>(builder.Configuration.GetSection("Convergence"));
builder.Services.AddHostedService<RunOrchestratorService>();
builder.Services.AddHostedService<OAuthCleanupBackgroundService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtelierDbContext>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();

builder.Services.AddAtelierAuth(builder.Configuration);
builder.Services.AddAtelierMcpAuth(builder.Configuration);
builder.Services.AddAtelierOAuth(builder.Configuration);

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
    o.AddPolicy("AdminPolicy", p =>
    {
        p.AuthenticationSchemes = new[] { CookieAuthenticationDefaults.AuthenticationScheme };
        p.RequireRole("admin");
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
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

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

// Seed the admin user from ATELIER_USER / ATELIER_PASSWORD_HASH env vars
using (var scope = app.Services.CreateScope())
{
    var logger      = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var userOpts    = scope.ServiceProvider.GetRequiredService<IOptions<AtelierUserOptions>>().Value;
    var userRepo    = scope.ServiceProvider.GetRequiredService<IAtelierUserRepository>();

    if (!string.IsNullOrEmpty(userOpts.Username) && !string.IsNullOrEmpty(userOpts.PasswordHash))
    {
        try
        {
            var existing = await userRepo.FindByUsernameAsync(userOpts.Username, CancellationToken.None);
            if (existing is null)
            {
                var admin = new AtelierUser(
                    UserId: Guid.NewGuid().ToString(),
                    Username: userOpts.Username,
                    PasswordHash: userOpts.PasswordHash,
                    Email: null,
                    IsActive: true,
                    IsAdmin: true,
                    CreatedAt: DateTimeOffset.UtcNow,
                    UpdatedAt: DateTimeOffset.UtcNow);
                await userRepo.AddAsync(admin, CancellationToken.None);
                logger.LogInformation("Admin user '{Username}' seeded from environment variables", userOpts.Username);
            }
            else if (!existing.IsAdmin || existing.PasswordHash != userOpts.PasswordHash)
            {
                var synced = existing with
                {
                    PasswordHash = userOpts.PasswordHash,
                    IsAdmin = true,
                    IsActive = true,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await userRepo.UpdateAsync(synced, CancellationToken.None);
                logger.LogInformation("Admin user '{Username}' synchronized from environment variables", userOpts.Username);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed admin user from environment variables");
        }
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

// Register OnStarting BEFORE auth middleware so it fires even when auth short-circuits.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if (ctx.Request.Path.StartsWithSegments("/mcp") &&
            ctx.Response.StatusCode == 401 &&
            !ctx.Response.Headers.ContainsKey("WWW-Authenticate"))
        {
            var issuer = ctx.RequestServices.GetRequiredService<IOptions<OAuthOptions>>().Value.Issuer;
            ctx.Response.Headers.WWWAuthenticate =
                $"Bearer resource_metadata=\"{issuer}/.well-known/oauth-protected-resource\"";
        }
        return Task.CompletedTask;
    });
    await next();
});

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
app.MapWellKnownEndpoints();
app.MapOAuthEndpoints();
app.MapSettingsEndpoints();
app.MapArtifactEndpoints();
app.MapProviderEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
