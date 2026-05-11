using System.Net;
using System.Net.Sockets;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Hubs;
using Geef.Atelier.Web.Notifications;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// Starts a real Kestrel web server with test overrides (configurable LLM client, test Postgres) for Playwright E2E tests.
/// </summary>
internal sealed class WebTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    /// <summary>The base URL where the test server is listening (e.g. http://127.0.0.1:54321).</summary>
    public string BaseUrl { get; }

    /// <summary>The semaphore that gates LLM calls. Release to allow pipeline execution; initial count is passed via <see cref="StartAsync"/>.</summary>
    public SemaphoreSlim Gate { get; }

    private WebTestHost(WebApplication app, string baseUrl, SemaphoreSlim gate)
    {
        _app    = app;
        BaseUrl = baseUrl;
        Gate    = gate;
    }

    /// <summary>
    /// Builds and starts the web server.
    /// </summary>
    /// <param name="fixture">Provides the Postgres connection string and migrations.</param>
    /// <param name="initialGateCount">Initial semaphore count. Use 0 to block pipeline; use a large value (e.g. 100) to run freely.</param>
    /// <param name="ct">Cancellation token for startup.</param>
    public static async Task<WebTestHost> StartAsync(
        PostgresFixture  fixture,
        int              initialGateCount = 100,
        CancellationToken ct = default)
    {
        var port = FindFreePort();
        var url  = $"http://127.0.0.1:{port}";
        var gate = new SemaphoreSlim(initialGateCount, int.MaxValue);
        var llmClient = new GatedFakeLlmClient(gate);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args            = ["--urls", url],
            EnvironmentName = "Development",
            ApplicationName = "Geef.Atelier.Web"
        });

        // LLM: gated fake client (no real HTTP calls)
        builder.Services.AddSingleton<ILlmClient>(llmClient);
        builder.Services.AddSingleton<IOptions<LlmOptions>>(Options.Create(new LlmOptions
        {
            ApiKey       = "test-key",
            DefaultModel = "test-model",
            Actors       = new Dictionary<string, LlmOptions.ActorConfig>
            {
                ["Executor"]              = new() { Model = "test-model" },
                ["BriefingTreueReviewer"] = new() { Model = "test-model" },
                ["KlarheitReviewer"]      = new() { Model = "test-model" }
            }
        }));

        // DB: fixture Postgres (already migrated by PostgresFixture)
        builder.Services.AddDbContext<AtelierDbContext>(opts =>
            opts.UseNpgsql(fixture.ConnectionString));

        builder.Services.AddAtelierPersistence();
        builder.Services.AddAtelierApplication();

        // Orchestrator: fast polling for tests
        builder.Services.Configure<OrchestratorOptions>(o =>
        {
            o.PollingInterval            = TimeSpan.FromMilliseconds(150);
            o.MaxConcurrentRuns          = 10;
            o.CancellationPollingInterval = TimeSpan.FromMilliseconds(200);
        });
        builder.Services.AddHostedService<RunOrchestratorService>();

        // SignalR + Notifier
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();

        // Blazor Server
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AtelierDbContext>().Database.MigrateAsync(ct);

        app.UseAntiforgery();
        app.MapHub<RunHub>("/hubs/runs");
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        await app.StartAsync(ct);

        return new WebTestHost(app, url, gate);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
