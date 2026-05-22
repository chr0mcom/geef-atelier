using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Tests for <see cref="AcademicSearchGroundingProvider"/> — covers result building,
/// citation construction, source selection, context formatting, and persistence.
/// </summary>
public sealed class AcademicSearchGroundingProviderTests
{
    private static readonly AcademicPaper PaperWithArxiv = new(
        Title: "Large Language Models",
        Authors: "Alice Smith; Bob Jones",
        Abstract: "We study LLMs at scale.",
        Doi: null,
        ArxivId: "2301.12345",
        Url: "http://arxiv.org/abs/2301.12345",
        PublishedDate: new DateTimeOffset(2023, 1, 18, 0, 0, 0, TimeSpan.Zero));

    private static readonly AcademicPaper PaperWithDoi = new(
        Title: "Transformer Architectures",
        Authors: "Carol Lee",
        Abstract: "Transformers explained.",
        Doi: "10.1234/test.999",
        ArxivId: null,
        Url: "https://doi.org/10.1234/test.999",
        PublishedDate: null);

    private static readonly AcademicPaper PaperMinimal = new(
        Title: "Minimal Paper",
        Authors: null,
        Abstract: null,
        Doi: null,
        ArxivId: null,
        Url: null,
        PublishedDate: null);

    [Fact]
    public async Task EnrichAsync_ReturnsPapers_FromSelectedSource()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv, PaperWithDoi]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public async Task EnrichAsync_EmptyResult_ReturnsEmptyContextAndNoCitations()
    {
        var (provider, _) = BuildProvider([]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Empty(result.Citations);
        Assert.Equal(string.Empty, result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_Citation_UsesArxivIdAsDocumentReference()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("arXiv:2301.12345", result.Citations[0].DocumentReference);
    }

    [Fact]
    public async Task EnrichAsync_Citation_UsesDoiAsDocumentReference_WhenNoArxivId()
    {
        var (provider, _) = BuildProvider([PaperWithDoi]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("DOI:10.1234/test.999", result.Citations[0].DocumentReference);
    }

    [Fact]
    public async Task EnrichAsync_Citation_UsesUrlAsDocumentReference_WhenNoDoi_AndNoArxivId()
    {
        var paperWithUrl = PaperMinimal with { Url = "https://example.com/paper" };
        var (provider, _) = BuildProvider([paperWithUrl]);
        var result = await provider.EnrichAsync("test", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("https://example.com/paper", result.Citations[0].DocumentReference);
    }

    [Fact]
    public async Task EnrichAsync_Citation_TitleMatchesPaperTitle()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("Large Language Models", result.Citations[0].Title);
    }

    [Fact]
    public async Task EnrichAsync_Snippet_IsTruncatedTo300Chars_ForLongAbstracts()
    {
        var longAbstract = new string('x', 500);
        var paper = PaperWithArxiv with { Abstract = longAbstract };
        var (provider, _) = BuildProvider([paper]);
        var result = await provider.EnrichAsync("q", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(301, result.Citations[0].Snippet?.Length); // 300 chars + "…"
        Assert.EndsWith("…", result.Citations[0].Snippet!);
    }

    [Fact]
    public async Task EnrichAsync_Context_ContainsSourceHeader()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make("semantic-scholar"), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("[Academic research context — source: semantic-scholar]", result.EnrichedContext);
        Assert.Contains("[End of academic research context]", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_Context_ContainsPaperTitle()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("Large Language Models", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_Context_ContainsAuthors_WhenPresent()
    {
        var (provider, _) = BuildProvider([PaperWithArxiv]);
        var result = await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("Alice Smith; Bob Jones", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_PersistsConsultation_WithCorrectRunId()
    {
        var repo = new InMemoryConsultationRepo();
        var (provider, _) = BuildProvider([PaperWithArxiv], repo);
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), runId, CancellationToken.None);

        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal(runId, stored[0].RunId);
    }

    [Fact]
    public async Task EnrichAsync_PersistsConsultation_WithProviderName()
    {
        var repo = new InMemoryConsultationRepo();
        var (provider, _) = BuildProvider([PaperWithArxiv], repo);
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("LLMs", AcademicProfileBuilder.Make(), runId, CancellationToken.None);

        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Equal("test-academic", stored[0].GroundingProviderName);
    }

    [Fact]
    public async Task EnrichAsync_FallsBackToSemanticScholar_ForUnknownSource()
    {
        var ssSource = new FakeAcademicSource("semantic-scholar", [PaperWithArxiv]);
        var (provider, _) = BuildProviderWithSources([ssSource], null);
        var profile = AcademicProfileBuilder.Make("unknown-source");

        var result = await provider.EnrichAsync("q", profile, Guid.NewGuid(), CancellationToken.None);
        Assert.Single(result.Citations);
    }

    [Fact]
    public async Task EnrichAsync_UsesNamedSource_WhenExplicitlyConfigured()
    {
        var arxivSource = new FakeAcademicSource("arxiv", [PaperWithArxiv, PaperWithDoi]);
        var ssSource    = new FakeAcademicSource("semantic-scholar", []);
        var (provider, _) = BuildProviderWithSources([ssSource, arxivSource], null);
        var profile = AcademicProfileBuilder.Make("arxiv");

        var result = await provider.EnrichAsync("q", profile, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public void ProviderType_IsAcademicSearch()
    {
        var (provider, _) = BuildProvider([]);
        Assert.Equal("academic-search", provider.ProviderType);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (AcademicSearchGroundingProvider, InMemoryConsultationRepo) BuildProvider(
        IReadOnlyList<AcademicPaper> papers,
        InMemoryConsultationRepo? repo = null)
    {
        var source = new FakeAcademicSource("semantic-scholar", papers);
        return BuildProviderWithSources([source], repo);
    }

    private static (AcademicSearchGroundingProvider, InMemoryConsultationRepo) BuildProviderWithSources(
        IReadOnlyList<IAcademicSource> sources,
        InMemoryConsultationRepo? repo)
    {
        repo ??= new InMemoryConsultationRepo();
        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => repo);
        var provider = new AcademicSearchGroundingProvider(
            sources,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AcademicSearchGroundingProvider>.Instance);
        return (provider, repo);
    }
}
