using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Notifications;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Builds and manages an <see cref="IHost"/> with <see cref="RunOrchestratorService"/> registered,
/// using the provided <see cref="PostgresFixture"/> database and a custom <see cref="ILlmClient"/>.
/// </summary>
internal sealed class OrchestratorTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    /// <summary>
    /// Initializes a new <see cref="OrchestratorTestHost"/> with the given fixture, client, and optional options.
    /// </summary>
    public OrchestratorTestHost(
        PostgresFixture      fixture,
        ILlmClient           llmClient,
        OrchestratorOptions? options = null)
    {
        var opts = options ?? new OrchestratorOptions
        {
            PollingInterval            = TimeSpan.FromMilliseconds(100),
            MaxConcurrentRuns          = 5,
            CancellationPollingInterval = TimeSpan.FromMilliseconds(200)
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<AtelierDbContext>(opt =>
                    opt.UseNpgsql(fixture.ConnectionString));
                services.AddAtelierPersistence();
                services.AddAtelierApplication();

                // Provide fake LLM client for tests.
                services.AddSingleton(llmClient);
                // LlmOptions with test defaults.
                services.AddSingleton<IOptions<LlmOptions>>(
                    Options.Create(new LlmOptions
                    {
                        ApiKey       = "test-key",
                        DefaultModel = "test-model",
                        Actors = new Dictionary<string, LlmOptions.ActorConfig>
                        {
                            ["Executor"]              = new() { Model = "test-model" },
                            ["BriefingTreueReviewer"] = new() { Model = "test-model" },
                            ["KlarheitReviewer"]      = new() { Model = "test-model" }
                        }
                    }));
                services.Configure<OrchestratorOptions>(o =>
                {
                    o.PollingInterval            = opts.PollingInterval;
                    o.MaxConcurrentRuns          = opts.MaxConcurrentRuns;
                    o.CancellationPollingInterval = opts.CancellationPollingInterval;
                });
                services.AddSingleton<IOptions<ConvergenceOptions>>(
                    Options.Create(new ConvergenceOptions()));
                services.AddSingleton<IRunNotifier, NoOpRunNotifier>();
                services.AddHostedService<RunOrchestratorService>();
            })
            .Build();
    }

    /// <summary>Starts the underlying host.</summary>
    public async Task StartAsync(CancellationToken ct = default) => await _host.StartAsync(ct);

    /// <summary>Stops the underlying host.</summary>
    public async Task StopAsync(CancellationToken ct = default) => await _host.StopAsync(ct);

    /// <summary>Exposes the root <see cref="IServiceScopeFactory"/> for creating test scopes.</summary>
    public IServiceScopeFactory ScopeFactory => _host.Services.GetRequiredService<IServiceScopeFactory>();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try { await _host.StopAsync(TimeSpan.FromSeconds(5)); } catch { /* best-effort */ }
        _host.Dispose();
    }
}
