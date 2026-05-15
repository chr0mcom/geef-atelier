using System.Net;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class TavilyGroundingProviderTests
{
    private const string ValidTavilyResponse = """
        {
          "answer": "Open-source LLMs have improved significantly in 2026.",
          "results": [
            { "title": "Best Open LLMs 2026", "url": "https://example.com/1", "content": "Llama 4 leads the pack with 128k context.", "score": 0.95 },
            { "title": "LLM Landscape Report", "url": "https://example.com/2", "content": "Mistral and Gemma are strong contenders.", "score": 0.87 }
          ]
        }
        """;

    [Fact]
    public async Task EnrichAsync_ReturnsGroundingResult_WithCitations()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse));

        var result = await provider.EnrichAsync("test briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("tavily-basic", result.ProviderName);
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal("Best Open LLMs 2026", result.Citations[0].Title);
        Assert.Equal("https://example.com/1", result.Citations[0].Url);
        Assert.Equal(0.95, result.Citations[0].RelevanceScore);
    }

    [Fact]
    public async Task EnrichAsync_IncludesSynthesizedAnswer_InEnrichedContext()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse));

        var result = await provider.EnrichAsync("test briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("Tavily synthesized answer:", result.EnrichedContext);
        Assert.Contains("Open-source LLMs have improved", result.EnrichedContext);
        Assert.Contains("[Web research context]", result.EnrichedContext);
        Assert.Contains("[End of web research context]", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_CalculatesBasicCostCorrectly()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse));
        var opts = new TavilyOptions { ApiKey = "tvly-test", BasicSearchCostUsd = 0.001, UsdToEurRate = 0.92 };

        var result = await provider.EnrichAsync("test briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(result.CostEur);
        // basic: 0.001 * 0.92 = 0.00092
        Assert.True(result.CostEur > 0);
    }

    [Fact]
    public async Task EnrichAsync_PersistsGroundingConsultation()
    {
        var consultationRepo = new InMemoryGroundingConsultationRepository();
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse), consultationRepo);
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("test briefing", SystemCrew.TavilyBasicProfile, runId, CancellationToken.None);

        var stored = await consultationRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal(runId, stored[0].RunId);
        Assert.Equal("tavily-basic", stored[0].GroundingProviderName);
        Assert.Equal("test briefing", stored[0].Query);
        Assert.Equal(2, stored[0].Citations.Count);
    }

    [Fact]
    public async Task EnrichAsync_ThrowsHttpRequestException_OnNon200Response()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Fail(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.EnrichAsync("briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EnrichAsync_ThrowsInvalidOperationException_WhenApiKeyMissing()
    {
        var opts = new TavilyOptions { ApiKey = "" };
        var handler = FakeHttpHandler.Ok(ValidTavilyResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.tavily.com") };
        var consultationRepo = new InMemoryGroundingConsultationRepository();
        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);
        var provider = new TavilyGroundingProvider(
            new FakeHttpClientFactory(httpClient),
            Options.Create(opts),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TavilyGroundingProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnrichAsync("briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void ProviderType_IsTavily()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse));
        Assert.Equal("tavily", provider.ProviderType);
    }

    // --- relevance-score filtering ---

    private const string LowRelevanceResponse = """
        {
          "answer": "Misleading synthesized answer derived from off-topic sources.",
          "results": [
            { "title": "Vierfarbensatz", "url": "https://x/1", "content": "map coloring", "score": 0.124 },
            { "title": "Travel catalog", "url": "https://x/2", "content": "vacations", "score": 0.0006 }
          ]
        }
        """;

    private const string MixedRelevanceResponse = """
        {
          "answer": "Partially relevant answer.",
          "results": [
            { "title": "Relevant hit", "url": "https://x/1", "content": "on topic", "score": 0.88 },
            { "title": "Noise", "url": "https://x/2", "content": "off topic", "score": 0.10 }
          ]
        }
        """;

    [Fact]
    public async Task EnrichAsync_DropsAllResultsBelowThreshold_AndDiscardsSynthesizedAnswer()
    {
        var (provider, repo) = BuildProvider(FakeHttpHandler.Ok(LowRelevanceResponse));
        var runId = Guid.NewGuid();

        var result = await provider.EnrichAsync("briefing", SystemCrew.TavilyBasicProfile, runId, CancellationToken.None);

        Assert.Empty(result.Citations);
        Assert.Equal(string.Empty, result.EnrichedContext);
        Assert.DoesNotContain("Misleading", result.EnrichedContext);
        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Empty(stored[0].Citations);
    }

    [Fact]
    public async Task EnrichAsync_KeepsOnlyResultsAtOrAboveThreshold()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(MixedRelevanceResponse));

        var result = await provider.EnrichAsync("briefing", SystemCrew.TavilyBasicProfile, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(result.Citations);
        Assert.Equal("Relevant hit", result.Citations[0].Title);
        Assert.Contains("Partially relevant answer.", result.EnrichedContext);
    }

    // --- LLM query extraction ---

    [Fact]
    public async Task EnrichAsync_SkipsWebSearch_WhenExtractorReturnsNoSearch()
    {
        var handler = FakeHttpHandler.Ok(ValidTavilyResponse);
        var extractor = new FakeQueryExtractor(new GroundingQuery(ShouldSearch: false, Query: string.Empty));
        var (provider, repo) = BuildProvider(handler, queryExtractor: extractor);
        var runId = Guid.NewGuid();

        var result = await provider.EnrichAsync("a pure math puzzle", SystemCrew.TavilyBasicProfile, runId, CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Empty(result.Citations);
        Assert.Equal(string.Empty, result.EnrichedContext);
        Assert.Null(result.CostEur);
        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Empty(stored[0].Citations);
        Assert.Equal("a pure math puzzle", stored[0].Query);
    }

    [Fact]
    public async Task EnrichAsync_UsesRefinedQuery_FromExtractor()
    {
        var extractor = new FakeQueryExtractor(new GroundingQuery(ShouldSearch: true, Query: "refined focused query"));
        var (provider, repo) = BuildProvider(FakeHttpHandler.Ok(ValidTavilyResponse), queryExtractor: extractor);
        var runId = Guid.NewGuid();

        var result = await provider.EnrichAsync("verbose briefing with tone instructions", SystemCrew.TavilyBasicProfile, runId, CancellationToken.None);

        Assert.Equal(2, result.Citations.Count);
        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Equal("refined focused query", stored[0].Query);
        Assert.Equal("verbose briefing with tone instructions", extractor.LastBriefing);
    }

    // --- helpers ---

    private static (TavilyGroundingProvider, InMemoryGroundingConsultationRepository) BuildProvider(
        FakeHttpHandler handler,
        InMemoryGroundingConsultationRepository? repo = null,
        IGroundingQueryExtractor? queryExtractor = null)
    {
        repo ??= new InMemoryGroundingConsultationRepository();
        var opts = new TavilyOptions
        {
            ApiKey = "tvly-test-key",
            BasicSearchCostUsd = 0.001,
            AdvancedSearchCostUsd = 0.002,
            UsdToEurRate = 0.92,
            RequestTimeoutSeconds = 30,
            DefaultMinRelevanceScore = 0.4,
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.tavily.com") };
        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => repo);
        var provider = new TavilyGroundingProvider(
            new FakeHttpClientFactory(httpClient),
            Options.Create(opts),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TavilyGroundingProvider>.Instance,
            queryExtractor);
        return (provider, repo);
    }

    private sealed class FakeQueryExtractor(GroundingQuery result) : IGroundingQueryExtractor
    {
        public string? LastBriefing { get; private set; }

        public Task<GroundingQuery> ExtractAsync(string briefingText, CancellationToken ct)
        {
            LastBriefing = briefingText;
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryGroundingConsultationRepository : IGroundingConsultationRepository
    {
        private readonly List<GroundingConsultation> _store = [];

        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>(_store.Where(c => c.RunId == runId).ToList());
    }
}
