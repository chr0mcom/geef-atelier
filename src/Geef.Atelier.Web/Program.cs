using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Runs;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Embeddings;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Intake;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Infrastructure.Knowledge;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Runs;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.TemplateStudio;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Infrastructure.Orders;
using Geef.Atelier.Infrastructure.Tools;
using Geef.Atelier.Mcp;
using Geef.Atelier.Web.Auth;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Endpoints;
using Geef.Atelier.Web.Health;
using Geef.Atelier.Web.Notifications;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

if (SourceExtractionWorker.IsRequested(args))
    return await SourceExtractionWorker.RunAsync(args, Console.OpenStandardOutput(), Console.Error);

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
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            LlmHttpRetry.MayRetry(args.Outcome.Result)
            && Microsoft.Extensions.Http.Resilience.HttpClientResiliencePredicates.IsTransient(args.Outcome));
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

builder.Services.AddGroundingProviders(builder.Configuration);
builder.Services.AddLearningSwitch(builder.Configuration);

// Tool execution infrastructure (IToolExecutor + IToolSchemaProvider)
builder.Services.AddToolExecutor();

// MCP client infrastructure (outbound connections to remote MCP servers)
builder.Services.AddAtelierMcpClient();

builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<CostTrackingOptions>(builder.Configuration.GetSection("CostTracking"));
builder.Services.AddSingleton<IPricingCatalog, PricingCatalog>();
builder.Services.AddSingleton<DocsService>();
builder.Services.AddTemplateStudio(builder.Configuration);
builder.Services.AddFinalizers(builder.Configuration);
builder.Services.AddCrewComposition();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection("Orchestrator"));
builder.Services.Configure<ConvergenceOptions>(builder.Configuration.GetSection("Convergence"));
builder.Services.Configure<Geef.Atelier.Core.Configuration.EnvReferenceOptions>(
    builder.Configuration.GetSection(Geef.Atelier.Core.Configuration.EnvReferenceOptions.SectionName));
builder.Services.Configure<SourceOptions>(builder.Configuration.GetSection(SourceOptions.SectionName));
builder.Services.PostConfigure<SourceOptions>(o =>
{
    if (string.IsNullOrWhiteSpace(o.ExtractionWorkerAssembly))
        o.ExtractionWorkerAssembly = typeof(Program).Assembly.Location;
});
builder.Services.Configure<OrderOptions>(builder.Configuration.GetSection(OrderOptions.SectionName));
builder.Services.Configure<IntakeOptions>(builder.Configuration.GetSection(IntakeOptions.SectionName));
builder.Services.Configure<DirectOptions>(builder.Configuration.GetSection(DirectOptions.SectionName));
builder.Services.AddSingleton<IRunFactCheckModeStore, RunFactCheckModeStore>();
builder.Services.AddSources(builder.Configuration);
builder.Services.AddProfiles();
builder.Services.AddOrderIntakeServices();
builder.Services.AddOrderIntake();
builder.Services.AddOrderExport();
builder.Services.AddRequestTimeouts();
builder.Services.AddPipelineAssemblers();
builder.Services.AddHostedService<RunOrchestratorService>();
builder.Services.AddHostedService<OAuthCleanupBackgroundService>();
builder.Services.Configure<Geef.Atelier.Core.Configuration.PackGcOptions>(
    builder.Configuration.GetSection(Geef.Atelier.Core.Configuration.PackGcOptions.SectionName));
builder.Services.AddHostedService<PackArchivalBackgroundService>();
builder.Services.AddHostedService<ModelCatalogWarmUpService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtelierDbContext>()
    .AddCheck<PendingMigrationsHealthCheck>("pending-migrations");

builder.Services.AddRunLiveUpdates();

builder.Services.AddAtelierAuth(builder.Configuration);
builder.Services.AddAtelierMcpAuth(builder.Configuration);
builder.Services.AddAtelierOAuth(builder.Configuration);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath        = "/login";
        o.LogoutPath       = "/auth/logout";
        o.AccessDeniedPath = "/access-denied";
        o.ExpireTimeSpan   = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly  = true;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Name     = "Atelier.Auth";
        o.Events.AnswerApiWithStatusCodes();
    })
    .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>(
        McpAuthorizationConstants.BearerScheme, _ => { });

builder.Services.AddAuthorization(o => o.AddAtelierPolicies());

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
builder.Services.AddScoped<IToolDefinitionService, ToolDefinitionService>();
builder.Services.AddScoped<ISpecializationPackService, SpecializationPackService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.MigrateDatabaseOrFailAsync();
app.Configuration.WarnAboutMissingGroundingKeys(app.Services.GetRequiredService<ILogger<Program>>());

await app.Services.SeedAdminUserFromOptionsAsync();

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
app.UseRequestTimeouts();

// Prevent caching of authenticated responses.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
        ctx.Response.Headers.CacheControl = "no-store, no-cache";
    await next();
});

app.MapHealthChecks("/health").AllowAnonymous();

app.MapRunHub();

app.MapMcp("/mcp").RequireAuthorization(McpAuthorizationConstants.McpPolicy);

app.MapAuthEndpoints();
app.MapWellKnownEndpoints();
app.MapOAuthEndpoints();
app.MapSettingsEndpoints();
app.MapArtifactEndpoints();
app.MapProviderEndpoints();
app.MapSourceEndpoints();
app.MapOrderEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;

// Required for WebApplicationFactory in integration tests
public partial class Program { }
