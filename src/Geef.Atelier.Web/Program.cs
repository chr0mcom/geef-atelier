using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Anthropic LLM client — ApiKey is read lazily; set Anthropic__ApiKey env-var for real calls.
builder.Services.AddAnthropicClient(builder.Configuration)
    .AddStandardResilienceHandler();

builder.Services.AddDbContext<AtelierDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAtelierPersistence();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection("Orchestrator"));
builder.Services.AddHostedService<RunOrchestratorService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtelierDbContext>();

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

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();

app.MapHealthChecks("/health");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
