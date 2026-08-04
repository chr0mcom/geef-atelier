using System.Net;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Unit tests for <see cref="ModelCatalog"/>.
/// HTTP providers are exercised through a fake <see cref="IHttpClientFactory"/>; CLI providers go
/// through a fake <see cref="ICliModelSource"/>, because what is under test there is caching,
/// merging and provenance — not transport, which CliProxyModelSourceTests covers.
/// </summary>
public sealed class ModelCatalogTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /// <summary>
    /// Advanceable clock, so the expiry of the alias mapping can be crossed deterministically
    /// instead of only ever being observed from the near side of it.
    /// </summary>
    private readonly TestClock Clock = new();

    public void Dispose() => _cache.Dispose();

    private ModelCatalog BuildCatalog(
        HttpMessageHandler handler,
        Provider? provider = null,
        ICliModelSource? cliSource = null,
        ModelCatalogOptions? options = null)
    {
        var resolvedProvider = provider ?? SystemProviders.OpenRouter;
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var scopeFactory = BuildScopeFactory(resolvedProvider);
        return new ModelCatalog(
            factory,
            _cache,
            scopeFactory,
            cliSource ?? new FakeCliModelSource(null),
            Options.Create(options ?? new ModelCatalogOptions()),
            Clock,
            NullLogger<ModelCatalog>.Instance);
    }

    /// <summary>
    /// One deadline spans the whole call, so the source sees the budget minus what queueing and the
    /// provider lookup already consumed — close to the configured value, never more than it.
    /// </summary>
    private static void AssertBudgetLeft(int configuredSeconds, TimeSpan actual)
    {
        var configured = TimeSpan.FromSeconds(configuredSeconds);
        Assert.True(actual <= configured, $"{actual} must not exceed the configured budget {configured}");
        Assert.True(actual > configured - TimeSpan.FromSeconds(1), $"{actual} is far below {configured}");
    }

    private static IServiceScopeFactory BuildScopeFactory(Provider? provider)
    {
        var fakeService = new FakeProviderService(provider);
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(fakeService);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }


    [Fact]
    public async Task ListModelsAsync_ReturnsApiModels_WhenEndpointSucceeds()
    {
        var handler = FakeHttpHandler.Ok("""
            {"object":"list","data":[
                {"id":"anthropic/claude-opus-4.8","object":"model"},
                {"id":"google/gemini-2.5-flash","object":"model"}
            ]}
            """);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "anthropic/claude-opus-4.8");
        Assert.Contains(models, m => m.Id == "google/gemini-2.5-flash");
    }

    [Fact]
    public async Task ListModelsAsync_MarksRecommended_FromStaticFallback()
    {
        var handler = FakeHttpHandler.Ok("""
            {"object":"list","data":[
                {"id":"anthropic/claude-opus-4.8","object":"model"},
                {"id":"some/unknown-model","object":"model"}
            ]}
            """);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        var opus = models.Single(m => m.Id == "anthropic/claude-opus-4.8");
        var unknown = models.Single(m => m.Id == "some/unknown-model");
        Assert.True(opus.IsRecommended);
        Assert.False(unknown.IsRecommended);
        Assert.False(catalog.IsUsingFallback("openrouter"));
        Assert.Equal(ModelCatalogSource.Live, catalog.GetSource("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesFallback_WhenEndpointFails()
    {
        var handler = FakeHttpHandler.Fail(HttpStatusCode.ServiceUnavailable);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        Assert.True(models.Count > 0);
        Assert.Equal(StaticModelFallback.ForOpenRouter.Count, models.Count);
        Assert.True(catalog.IsUsingFallback("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesFallback_WhenProviderUnknown()
    {
        var handler = FakeHttpHandler.Fail(HttpStatusCode.NotFound);
        var scopeFactory = BuildScopeFactory(null); // no provider returned
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var catalog = new ModelCatalog(
            factory, _cache, scopeFactory, new FakeCliModelSource(null),
            Options.Create(new ModelCatalogOptions()), Clock, NullLogger<ModelCatalog>.Instance);

        await catalog.ListModelsAsync("openrouter");

        Assert.True(catalog.IsUsingFallback("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesCacheOnSecondCall()
    {
        var handler = FakeHttpHandler.Ok("""{"object":"list","data":[{"id":"anthropic/claude-opus-4.8"}]}""");
        var catalog = BuildCatalog(handler);

        await catalog.ListModelsAsync("openrouter");
        await catalog.ListModelsAsync("openrouter");

        Assert.Equal(1, handler.CallCount); // second call hit cache
    }

    [Fact]
    public async Task RefreshAsync_BypassesCache()
    {
        var handler = FakeHttpHandler.Ok("""{"object":"list","data":[{"id":"anthropic/claude-opus-4.8"}]}""");
        var catalog = BuildCatalog(handler);

        await catalog.ListModelsAsync("openrouter");
        await catalog.RefreshAsync("openrouter");

        Assert.Equal(2, handler.CallCount); // Refresh triggered a second fetch
    }


    [Fact]
    public async Task CliProvider_ServesTheLiveListAndReportsLive()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-latest", "claude-opus-5"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-5" }));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var models = await catalog.ListModelsAsync("claude-cli");

        Assert.Contains(models, m => m.Id == "claude-opus-5");
        Assert.Contains(models, m => m.Id == "claude-opus-latest");
        Assert.Equal(ModelCatalogSource.Live, catalog.GetSource("claude-cli"));
        Assert.False(catalog.IsUsingFallback("claude-cli"));
    }

    [Fact]
    public async Task CliProvider_FallsBackToConfiguredList_WhenSourceFails()
    {
        var source = new FakeCliModelSource(null);
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var models = await catalog.ListModelsAsync("claude-cli");
        var ids = models.Select(m => m.Id).ToList();

        foreach (var configured in CliProviderSettings.FromSettings(SystemProviders.ClaudeCli.Settings).Models)
            Assert.Contains(configured, ids);

        Assert.True(catalog.IsUsingFallback("claude-cli"));
        Assert.Equal(ModelCatalogSource.Fallback, catalog.GetSource("claude-cli"));
    }

    [Fact]
    public async Task CliProvider_FallsBackToTheStaticList_WhenTheSourceFailsAndNothingIsConfigured()
    {
        var bareProvider = SystemProviders.ClaudeCli with
        {
            Settings = new Dictionary<string, System.Text.Json.JsonElement>(SystemProviders.ClaudeCli.Settings)
            {
                ["models"] = System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone(),
            },
        };
        var catalog = BuildCatalog(
            FakeHttpHandler.Fail(HttpStatusCode.NotFound), bareProvider, new FakeCliModelSource(null));

        var ids = (await catalog.ListModelsAsync("claude-cli")).Select(m => m.Id).ToList();

        Assert.NotEmpty(ids);
        foreach (var known in StaticModelFallback.ForClaudeCli)
            Assert.Contains(known.Id, ids);
    }

    [Fact]
    public async Task AnInteractiveCallDoesNotInheritASlowNeighboursBudget()
    {
        const string liveOnlyId = "claude-opus-from-the-gateway";
        Assert.DoesNotContain(StaticModelFallback.ForClaudeCli, m => m.Id == liveOnlyId);

        var source = new SwitchableCliModelSource(new CliModelCatalog(
            [liveOnlyId], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(
            FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source,
            new ModelCatalogOptions { InteractiveTimeoutSeconds = 1, WarmUpTimeoutSeconds = 240 });

        await catalog.ListModelsAsync("claude-cli");
        Assert.Equal(ModelCatalogSource.Live, catalog.GetSource("claude-cli"));

        source.DelayMs = 4000;
        var warmUp = catalog.WarmUpAsync("claude-cli");
        await Task.Delay(100);

        var started = DateTimeOffset.UtcNow;
        var interactive = await catalog.ListModelsAsync("claude-cli");
        var waited = DateTimeOffset.UtcNow - started;

        Assert.Contains(interactive, m => m.Id == liveOnlyId);
        Assert.True(waited < TimeSpan.FromSeconds(2),
            $"interactive call waited {waited}; it queued behind the warm-up instead of reading the cache");

        Assert.Equal(ModelCatalogSource.Live, catalog.GetSource("claude-cli"));

        await warmUp;
    }

    [Fact]
    public async Task CliProvider_MergesKnownIdsIntoTheFallbackListToo()
    {
        var source = new FakeCliModelSource(null);
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var ids = (await catalog.ListModelsAsync("claude-cli")).Select(m => m.Id).ToList();

        foreach (var known in StaticModelFallback.ForClaudeCli)
            Assert.Contains(known.Id, ids);
    }

    [Fact]
    public async Task CliProvider_DegradedSourceIsAFallback_NotALiveAnswer()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-4-8"], IsLive: false, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(ModelCatalogSource.Fallback, catalog.GetSource("claude-cli"));
        Assert.True(catalog.IsUsingFallback("claude-cli"));
    }

    [Fact]
    public async Task CliProvider_WithoutLiveSourceIsStaticByDesign_NotAFailure()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["gemini-3-1-pro"], IsLive: false, HasLiveSource: false, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.GeminiCli, source);

        await catalog.ListModelsAsync("gemini-cli");

        Assert.Equal(ModelCatalogSource.StaticByDesign, catalog.GetSource("gemini-cli"));
        Assert.False(catalog.IsUsingFallback("gemini-cli"));
    }

    [Fact]
    public async Task CliProvider_MergesLiveListWithKnownIds()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["gpt-9-nova"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.CodexCli, source);

        var ids = (await catalog.ListModelsAsync("codex-cli")).Select(m => m.Id).ToList();

        Assert.Contains("gpt-9-nova", ids);
        Assert.Contains(PreferredComposerModels.Reviewers.First(r => r.Provider == "codex-cli").Model, ids);
    }

    [Fact]
    public async Task DegradedSourceIsCachedWithTheShortFailureTtl()
    {
        var recording = new RecordingMemoryCache(_cache);
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-4-8"], IsLive: false, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = new ModelCatalog(
            new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound))),
            recording,
            BuildScopeFactory(SystemProviders.ClaudeCli),
            source,
            Options.Create(new ModelCatalogOptions { FailureTtlMinutes = 5, CliTtlHours = 24 }),
            Clock,
            NullLogger<ModelCatalog>.Instance);

        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(TimeSpan.FromMinutes(5), recording.LastAbsoluteExpirationRelativeToNow);
    }

    private static CliModelCatalog LiveOpus() => new(
        ["claude-opus-latest"], IsLive: true, HasLiveSource: true,
        new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-5" });

    [Theory]
    [InlineData(false)] // K1: transport failure — null
    [InlineData(true)]  // K2: HTTP 200 with x_source "degraded" and an empty alias map
    public async Task AnUntrustworthyAnswerKeepsTheLastKnownAliasMap(bool degradedRatherThanNull)
    {
        var answer = degradedRatherThanNull
            ? new CliModelCatalog(["claude-opus-4-8"], IsLive: false, HasLiveSource: true,
                new Dictionary<string, string>())
            : null;

        var live = new SwitchableCliModelSource(LiveOpus());
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, live);

        await catalog.ListModelsAsync("claude-cli");
        live.Result = answer;
        await catalog.RefreshAsync("claude-cli");

        Assert.Equal(ModelCatalogSource.Fallback, catalog.GetSource("claude-cli"));
        Assert.Equal("claude-opus-5", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
    }

    [Fact]
    public async Task TheAliasMapIsTrustedOnItsOwnExpiry_NotOnSomeListBeingCached()
    {
        var live = new SwitchableCliModelSource(LiveOpus());
        var catalog = BuildCatalog(
            FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, live,
            new ModelCatalogOptions { CliTtlHours = 1, FailureTtlMinutes = 5 });

        await catalog.ListModelsAsync("claude-cli");
        live.Result = null;
        await catalog.RefreshAsync("claude-cli");
        await catalog.RefreshAsync("claude-cli");

        Assert.Equal("claude-opus-5", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
    }

    /// <summary>
    /// The gateway calls an answer live as soon as one model family resolved, so a later partial
    /// run must add to what is known instead of dropping the families that resolved before.
    /// </summary>
    [Fact]
    public async Task APartialProbeAddsToTheKnownAliasesInsteadOfReplacingThem()
    {
        var source = new SwitchableCliModelSource(new CliModelCatalog(
            ["claude-opus-latest", "claude-sonnet-latest"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string>
            {
                ["claude-opus-latest"] = "claude-opus-5",
                ["claude-sonnet-latest"] = "claude-sonnet-5",
            }));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");

        source.Result = new CliModelCatalog(
            ["claude-opus-latest"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-6" });
        await catalog.RefreshAsync("claude-cli");

        Assert.Equal("claude-opus-6", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
        Assert.Equal("claude-sonnet-5", await catalog.ResolveModelAsync("claude-cli", "claude-sonnet-latest"));
    }

    /// <summary>
    /// The deadline spans the whole call, so a stage that hangs past the budget — here loading the
    /// provider — must end the call rather than run on top of the budget already spent.
    /// </summary>
    [Fact]
    public async Task AHangingProviderLookupCannotOutliveTheBudget()
    {
        var slowProviders = new SlowProviderService(SystemProviders.ClaudeCli, TimeSpan.FromSeconds(30));
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(slowProviders);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var catalog = new ModelCatalog(
            new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound))),
            _cache,
            scopeFactory,
            new FakeCliModelSource(null),
            Options.Create(new ModelCatalogOptions { InteractiveTimeoutSeconds = 1 }),
            Clock,
            NullLogger<ModelCatalog>.Instance);

        var started = DateTimeOffset.UtcNow;
        var models = await catalog.ListModelsAsync("claude-cli");
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.NotEmpty(models);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"the call ran {elapsed} past its 1 s budget");
    }

    /// <summary>
    /// The negative half of the expiry contract: once the stamp has passed, the mapping must stop
    /// being trusted and resolution must fall back to the alias, even though the entry is still
    /// in memory.
    /// </summary>
    [Fact]
    public async Task TheAliasMapStopsBeingTrustedOnceItsStampHasPassed()
    {
        var live = new SwitchableCliModelSource(LiveOpus());
        var catalog = BuildCatalog(
            FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, live,
            new ModelCatalogOptions { CliTtlHours = 1 });

        await catalog.ListModelsAsync("claude-cli");
        Assert.Equal("claude-opus-5", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));

        Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        Assert.Equal("claude-opus-latest", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
    }

    /// <summary>
    /// A provider with no alias layer is marked permanently, so its short-circuit must survive any
    /// amount of elapsed time.
    /// </summary>
    [Fact]
    public async Task TheNoAliasLayerMarkerNeverExpires()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["gemini-3-1-pro"], IsLive: false, HasLiveSource: false, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.GeminiCli, source);

        await catalog.ListModelsAsync("gemini-cli");
        Clock.Advance(TimeSpan.FromDays(400));

        Assert.Equal("gemini-3-1-pro", await catalog.ResolveModelAsync("gemini-cli", "gemini-3-1-pro"));
    }

    [Fact]
    public async Task AStaticByDesignProviderReportsNoAliasLayer()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["gemini-3-1-pro"], IsLive: false, HasLiveSource: false, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.GeminiCli, source);

        await catalog.ListModelsAsync("gemini-cli");

        Assert.Equal("gemini-3-1-pro", await catalog.ResolveModelAsync("gemini-cli", "gemini-3-1-pro"));
    }

    [Fact]
    public async Task TheKillSwitchClearsTheAliasMap()
    {
        var live = new SwitchableCliModelSource(LiveOpus());
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, live);
        await catalog.ListModelsAsync("claude-cli");

        var offCatalog = new ModelCatalog(
            new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound))),
            _cache,
            BuildScopeFactory(SystemProviders.ClaudeCli),
            live,
            Options.Create(new ModelCatalogOptions { CliLiveCatalogEnabled = false }),
            Clock,
            NullLogger<ModelCatalog>.Instance);

        await offCatalog.RefreshAsync("claude-cli");

        Assert.Equal("claude-opus-latest", await offCatalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
    }

    [Fact]
    public async Task ResolveModelAsync_NeverTriggersALookup()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-latest"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-5" }));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var resolved = await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest");

        Assert.Equal("claude-opus-latest", resolved);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task ResolveModelAsync_DoesNotFetchForHttpProviders()
    {
        var handler = FakeHttpHandler.Ok("""{"object":"list","data":[{"id":"anthropic/claude-opus-4.8"}]}""");
        var catalog = BuildCatalog(handler);

        await catalog.ListModelsAsync("openrouter");
        var callsAfterList = handler.CallCount;

        await catalog.ResolveModelAsync("openrouter", "anthropic/claude-opus-4.8");

        Assert.Equal(callsAfterList, handler.CallCount);
    }

    [Fact]
    public async Task CliProvider_RecommendedGroupIsNeverEmpty_EvenForAnUnknownLiveList()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["totally-unknown-1"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var models = await catalog.ListModelsAsync("claude-cli");

        Assert.Contains(models, m => m.IsRecommended);
    }

    [Fact]
    public async Task CliProvider_FirstRecommendedIsTheOpusAlias()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-latest", "claude-sonnet-latest", "claude-haiku-latest", "claude-opus-5"],
            IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var models = await catalog.ListModelsAsync("claude-cli");
        var firstRecommended = models.First(m => m.IsRecommended);

        Assert.Equal("claude-opus-latest", firstRecommended.Id);
    }

    [Fact]
    public async Task CliProvider_EmptyLiveListCountsAsFailure()
    {
        var source = new FakeCliModelSource(null);
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        var models = await catalog.ListModelsAsync("claude-cli");

        Assert.NotEmpty(models);
        Assert.Equal(ModelCatalogSource.Fallback, catalog.GetSource("claude-cli"));
    }

    [Fact]
    public async Task CliProvider_UsesCacheOnSecondCall()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");
        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task CliProvider_ConcurrentColdCallsHitTheSourceOnce()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()))
        { DelayMs = 30 };
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await Task.WhenAll(
            catalog.ListModelsAsync("claude-cli"),
            catalog.ListModelsAsync("claude-cli"));

        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task WarmUpAsync_AsksTheSourceToBypassItsOwnCache()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.WarmUpAsync("claude-cli");

        Assert.True(source.LastBypassProxyCache);
        AssertBudgetLeft(new ModelCatalogOptions().WarmUpTimeoutSeconds, source.LastTimeout);
    }

    [Fact]
    public async Task ListModelsAsync_UsesTheShortInteractiveBudget()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");

        AssertBudgetLeft(new ModelCatalogOptions().InteractiveTimeoutSeconds, source.LastTimeout);
    }

    [Fact]
    public async Task CliLiveCatalogDisabled_SkipsTheGatewayEntirely()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = BuildCatalog(
            FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source,
            new ModelCatalogOptions { CliLiveCatalogEnabled = false });

        var models = await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(0, source.CallCount);
        Assert.NotEmpty(models);
        Assert.Equal(ModelCatalogSource.StaticByDesign, catalog.GetSource("claude-cli"));
    }

    [Fact]
    public async Task FailedLookupIsCachedWithTheShortFailureTtl()
    {
        var recording = new RecordingMemoryCache(_cache);
        var source = new FakeCliModelSource(null);
        var catalog = new ModelCatalog(
            new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound))),
            recording,
            BuildScopeFactory(SystemProviders.ClaudeCli),
            source,
            Options.Create(new ModelCatalogOptions { FailureTtlMinutes = 5, CliTtlHours = 24 }),
            Clock,
            NullLogger<ModelCatalog>.Instance);

        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(TimeSpan.FromMinutes(5), recording.LastAbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task SuccessfulCliLookupIsCachedWithTheLongTtl()
    {
        var recording = new RecordingMemoryCache(_cache);
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-5"], IsLive: true, HasLiveSource: true, new Dictionary<string, string>()));
        var catalog = new ModelCatalog(
            new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound))),
            recording,
            BuildScopeFactory(SystemProviders.ClaudeCli),
            source,
            Options.Create(new ModelCatalogOptions { FailureTtlMinutes = 5, CliTtlHours = 24 }),
            Clock,
            NullLogger<ModelCatalog>.Instance);

        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal(TimeSpan.FromHours(24), recording.LastAbsoluteExpirationRelativeToNow);
    }


    [Fact]
    public async Task ResolveModelAsync_MapsAnAliasToItsConcreteModel()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-latest", "claude-opus-5"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-5" }));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");
        var resolved = await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest");

        Assert.Equal("claude-opus-5", resolved);
    }

    [Fact]
    public async Task ResolveModelAsync_LeavesUnknownIdsUnchanged()
    {
        var source = new FakeCliModelSource(new CliModelCatalog(
            ["claude-opus-latest"], IsLive: true, HasLiveSource: true,
            new Dictionary<string, string> { ["claude-opus-latest"] = "claude-opus-5" }));
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        await catalog.ListModelsAsync("claude-cli");

        Assert.Equal("claude-opus-4-8", await catalog.ResolveModelAsync("claude-cli", "claude-opus-4-8"));
        Assert.Equal("", await catalog.ResolveModelAsync("claude-cli", ""));
    }

    [Fact]
    public async Task ResolveModelAsync_KeepsTheAlias_WhenTheGatewayIsUnavailable()
    {
        var source = new FakeCliModelSource(null);
        var catalog = BuildCatalog(FakeHttpHandler.Fail(HttpStatusCode.NotFound), SystemProviders.ClaudeCli, source);

        Assert.Equal("claude-opus-latest", await catalog.ResolveModelAsync("claude-cli", "claude-opus-latest"));
    }


    /// <summary>
    /// Guards the ORDER and the recommended flags of the curated list, not its interaction with the
    /// gateway — the merge behaviour those flags feed into is covered by the CLI-provider tests above.
    /// </summary>
    [Fact]
    public void StaticFallback_ForClaudeCli_LeadsWithTheAlwaysLatestAliases()
    {
        var models = StaticModelFallback.ForClaudeCli;

        Assert.Equal("claude-opus-latest", models[0].Id);
        Assert.True(models[0].IsRecommended);
        Assert.All(models.Where(m => m.IsRecommended), m => Assert.EndsWith("-latest", m.Id));
    }

    [Fact]
    public void StaticFallback_ForGeminiCli_IsKnown()
    {
        Assert.NotEmpty(StaticModelFallback.For("gemini-cli"));
    }

    [Fact]
    public void StaticFallback_For_UnknownProvider_ReturnsEmpty()
    {
        Assert.Empty(StaticModelFallback.For("unknown-xyz"));
    }
}


/// <summary>Source whose answer can change between calls, for transition tests.</summary>
/// <summary>Manually advanceable <see cref="TimeProvider"/> for deterministic expiry tests.</summary>
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class SwitchableCliModelSource(CliModelCatalog? initial) : ICliModelSource
{
    public CliModelCatalog? Result { get; set; } = initial;

    /// <summary>Makes a lookup slow, so tests can hold the provider lock deterministically.</summary>
    public int DelayMs { get; set; }

    public async Task<CliModelCatalog?> TryGetCatalogAsync(
        string providerName, TimeSpan timeout, bool bypassProxyCache, CancellationToken ct = default)
    {
        if (DelayMs > 0)
            await Task.Delay(DelayMs, ct);

        return Result;
    }
}

internal sealed class FakeCliModelSource(CliModelCatalog? result) : ICliModelSource
{
    public int CallCount { get; private set; }
    public bool LastBypassProxyCache { get; private set; }
    public TimeSpan LastTimeout { get; private set; }
    public int DelayMs { get; init; }

    public async Task<CliModelCatalog?> TryGetCatalogAsync(
        string providerName, TimeSpan timeout, bool bypassProxyCache, CancellationToken ct = default)
    {
        CallCount++;
        LastBypassProxyCache = bypassProxyCache;
        LastTimeout = timeout;
        if (DelayMs > 0)
            await Task.Delay(DelayMs, ct);
        return result;
    }
}

/// <summary>Records the expiration the catalog asks for, so TTL choices are assertable.</summary>
internal sealed class RecordingMemoryCache(IMemoryCache inner) : IMemoryCache
{
    public TimeSpan? LastAbsoluteExpirationRelativeToNow { get; private set; }

    public ICacheEntry CreateEntry(object key) => new RecordingEntry(inner.CreateEntry(key), this);

    public void Remove(object key) => inner.Remove(key);

    public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);

    public void Dispose() { }

    private sealed class RecordingEntry(ICacheEntry inner, RecordingMemoryCache owner) : ICacheEntry
    {
        public object Key => inner.Key;
        public object? Value { get => inner.Value; set => inner.Value = value; }
        public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration = value; }

        public TimeSpan? AbsoluteExpirationRelativeToNow
        {
            get => inner.AbsoluteExpirationRelativeToNow;
            set { inner.AbsoluteExpirationRelativeToNow = value; owner.LastAbsoluteExpirationRelativeToNow = value; }
        }

        public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration = value; }
        public IList<Microsoft.Extensions.Primitives.IChangeToken> ExpirationTokens => inner.ExpirationTokens;
        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
        public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority = value; }
        public long? Size { get => inner.Size; set => inner.Size = value; }

        public void Dispose() => inner.Dispose();
    }
}

internal sealed class FakeHttpHandler(HttpStatusCode statusCode, string? body) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    public static FakeHttpHandler Ok(string body) => new(HttpStatusCode.OK, body);
    public static FakeHttpHandler Fail(HttpStatusCode code) => new(code, null);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var resp = new HttpResponseMessage(statusCode);
        if (body is not null)
            resp.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return Task.FromResult(resp);
    }
}

internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>Provider service that hangs, to prove the deadline reaches this stage as well.</summary>
internal sealed class SlowProviderService(Provider provider, TimeSpan delay) : IProviderService
{
    public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Provider>>([provider]);

    public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await Task.Delay(delay, ct);
        return provider;
    }

    public Task<Provider> CreateCustomAsync(Provider p, CancellationToken ct = default) => Task.FromResult(p);
    public Task<Provider> UpdateCustomAsync(string name, Provider p, CancellationToken ct = default) => Task.FromResult(p);
    public Task DeleteCustomAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
        => Task.FromResult(new ConnectionTestResult(true, 0, null, null));
}

internal sealed class FakeProviderService(Provider? provider) : IProviderService
{
    public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Provider>>(provider is not null ? [provider] : []);

    public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(provider?.Name == name ? provider : null);

    public Task<Provider> CreateCustomAsync(Provider p, CancellationToken ct = default)
        => Task.FromResult(p);

    public Task<Provider> UpdateCustomAsync(string name, Provider p, CancellationToken ct = default)
        => Task.FromResult(p);

    public Task DeleteCustomAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
        => Task.FromResult(new ConnectionTestResult(true, 0, null, null));
}
