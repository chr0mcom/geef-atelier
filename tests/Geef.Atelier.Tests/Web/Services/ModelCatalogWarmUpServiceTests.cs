using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Scheduling;
using Geef.Atelier.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Services;

/// <summary>
/// Tests for the nightly model-catalog warm-up. The sweep is invoked directly so no test ever waits
/// on wall-clock time; the schedule itself is a pure function and tested separately.
/// </summary>
public sealed class ModelCatalogWarmUpServiceTests
{
    private static ModelCatalogWarmUpService Build(
        IModelCatalog catalog, IReadOnlyList<Provider> providers, ModelCatalogOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(new ListProviderService(providers));
        var sp = services.BuildServiceProvider();

        return new ModelCatalogWarmUpService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            catalog,
            Options.Create(options ?? new ModelCatalogOptions()),
            NullLogger<ModelCatalogWarmUpService>.Instance);
    }

    [Fact]
    public async Task WarmsEveryActiveCliProvider()
    {
        var catalog = new RecordingModelCatalog();
        var service = Build(catalog, [SystemProviders.ClaudeCli, SystemProviders.CodexCli]);

        await service.WarmUpAllAsync(CancellationToken.None);

        Assert.Equal(["claude-cli", "codex-cli"], catalog.WarmedProviders);
    }

    [Fact]
    public async Task SkipsHttpProviders()
    {
        var catalog = new RecordingModelCatalog();
        var service = Build(catalog, [SystemProviders.OpenRouter, SystemProviders.ClaudeCli, SystemProviders.Groq]);

        await service.WarmUpAllAsync(CancellationToken.None);

        Assert.Equal(["claude-cli"], catalog.WarmedProviders);
    }

    [Fact]
    public async Task OneFailingProviderDoesNotStopTheSweep()
    {
        var catalog = new RecordingModelCatalog { FailFor = "claude-cli" };
        var service = Build(catalog, [SystemProviders.ClaudeCli, SystemProviders.CodexCli]);

        await service.WarmUpAllAsync(CancellationToken.None);

        Assert.Contains("codex-cli", catalog.WarmedProviders);
    }

    [Fact]
    public async Task AFailingProviderListingIsSurvivable()
    {
        var catalog = new RecordingModelCatalog();
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(new ThrowingProviderService());
        var sp = services.BuildServiceProvider();

        var service = new ModelCatalogWarmUpService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            catalog,
            Options.Create(new ModelCatalogOptions()),
            NullLogger<ModelCatalogWarmUpService>.Instance);

        await service.WarmUpAllAsync(CancellationToken.None);

        Assert.Empty(catalog.WarmedProviders);
    }

    private sealed class RecordingModelCatalog : IModelCatalog
    {
        public List<string> WarmedProviders { get; } = [];
        public string? FailFor { get; init; }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default)
        {
            if (providerName == FailFor)
                throw new InvalidOperationException("gateway unreachable");

            WarmedProviders.Add(providerName);
            return Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        }

        public ModelCatalogSource GetSource(string providerName) => ModelCatalogSource.Live;

        public bool IsUsingFallback(string providerName) => false;

        public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
            => Task.FromResult(modelId);
    }

    private sealed class ListProviderService(IReadOnlyList<Provider> providers) : IProviderService
    {
        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult(providers);

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(providers.FirstOrDefault(p => p.Name == name));

        public Task<Provider> CreateCustomAsync(Provider p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<Provider> UpdateCustomAsync(string name, Provider p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, 0, null, null));
    }

    private sealed class ThrowingProviderService : IProviderService
    {
        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => throw new InvalidOperationException("database down");

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Provider?>(null);
        public Task<Provider> CreateCustomAsync(Provider p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<Provider> UpdateCustomAsync(string name, Provider p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, 0, null, null));
    }
}

/// <summary>Pure schedule arithmetic — no clock, no delay, no flakiness.</summary>
public sealed class NightlyScheduleCalculatorTests
{
    [Fact]
    public void ReturnsTodaysOccurrence_WhenItIsStillAhead()
    {
        var now = new DateTimeOffset(2026, 8, 4, 1, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15),
            NightlyScheduleCalculator.DelayUntilNext(now, 3, 15));
    }

    [Fact]
    public void RollsOverToTomorrow_WhenTheTimeHasPassed()
    {
        var now = new DateTimeOffset(2026, 8, 4, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(15),
            NightlyScheduleCalculator.DelayUntilNext(now, 3, 15));
    }

    [Fact]
    public void NeverReturnsZero_WhenNowIsExactlyTheTargetTime()
    {
        var now = new DateTimeOffset(2026, 8, 4, 3, 15, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromHours(24), NightlyScheduleCalculator.DelayUntilNext(now, 3, 15));
    }

    [Fact]
    public void ConvertsNonUtcInputToUtc()
    {
        var now = new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.FromHours(2)); // 01:00 UTC

        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15),
            NightlyScheduleCalculator.DelayUntilNext(now, 3, 15));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 999)]
    public void ClampsOutOfRangeInput(int hour, int minute)
    {
        var now = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        var delay = NightlyScheduleCalculator.DelayUntilNext(now, hour, minute);

        Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromHours(24));
        Assert.True(delay > TimeSpan.Zero);
    }
}
