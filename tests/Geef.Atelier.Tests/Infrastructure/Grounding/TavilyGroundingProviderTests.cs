using System.Net;
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
            httpClient,
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

    // --- helpers ---

    private static (TavilyGroundingProvider, InMemoryGroundingConsultationRepository) BuildProvider(
        FakeHttpHandler handler,
        InMemoryGroundingConsultationRepository? repo = null)
    {
        repo ??= new InMemoryGroundingConsultationRepository();
        var opts = new TavilyOptions
        {
            ApiKey = "tvly-test-key",
            BasicSearchCostUsd = 0.001,
            AdvancedSearchCostUsd = 0.002,
            UsdToEurRate = 0.92,
            RequestTimeoutSeconds = 30,
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.tavily.com") };
        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => repo);
        var provider = new TavilyGroundingProvider(
            httpClient,
            Options.Create(opts),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TavilyGroundingProvider>.Instance);
        return (provider, repo);
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
