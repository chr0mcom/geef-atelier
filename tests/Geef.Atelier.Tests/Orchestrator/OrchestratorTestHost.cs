using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Builds and manages an <see cref="IHost"/> with <see cref="RunOrchestratorService"/> registered,
/// using the provided <see cref="PostgresFixture"/> database and a custom <see cref="IAnthropicClient"/>.
/// </summary>
internal sealed class OrchestratorTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    /// <summary>
    /// Initializes a new <see cref="OrchestratorTestHost"/> with the given fixture, client, and optional options.
    /// </summary>
    public OrchestratorTestHost(
        PostgresFixture     fixture,
        IAnthropicClient    anthropicClient,
        OrchestratorOptions? options = null)
    {
        var opts = options ?? new OrchestratorOptions
        {
            PollingInterval   = TimeSpan.FromMilliseconds(100),
            MaxConcurrentRuns = 5
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<AtelierDbContext>(opt =>
                    opt.UseNpgsql(fixture.ConnectionString));
                services.AddAtelierPersistence();

                // Provide fake Anthropic client for tests (overrides any AddHttpClient registration)
                services.AddSingleton(anthropicClient);
                // AnthropicOptions uses init-only properties; wrap in IOptions directly
                services.AddSingleton<IOptions<AnthropicOptions>>(
                    Options.Create(new AnthropicOptions
                    {
                        ApiKey        = "test-key",
                        ExecutorModel = "test-model",
                        ReviewerModel = "test-model"
                    }));
                services.Configure<OrchestratorOptions>(o =>
                {
                    o.PollingInterval   = opts.PollingInterval;
                    o.MaxConcurrentRuns = opts.MaxConcurrentRuns;
                });
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
