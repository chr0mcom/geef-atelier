using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class StaticContextGroundingProviderTests
{
    private static IServiceScopeFactory CreateScopeFactory(
        FakeGroundingConsultationRepository? repo = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGroundingConsultationRepository>(repo ?? new FakeGroundingConsultationRepository());
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static GroundingProviderProfile MakeProfile(
        string? content = null,
        string? label = null,
        string name = "test-static") =>
        new(Name: name,
            DisplayName: "Test Static",
            Description: "",
            ProviderType: GroundingProviderTypes.StaticContext,
            ProviderSettings: BuildSettings(content, label),
            MaxQueriesPerRun: 1,
            IsSystem: false);

    private static Dictionary<string, string> BuildSettings(string? content, string? label)
    {
        var d = new Dictionary<string, string>();
        if (content is not null) d[GroundingProviderProfile.KeyStaticContent] = content;
        if (label is not null) d[GroundingProviderProfile.KeyStaticLabel] = label;
        return d;
    }

    // ── ProviderType ─────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_IsStaticContext()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        Assert.Equal(GroundingProviderTypes.StaticContext, provider.ProviderType);
    }

    // ── EnrichAsync with content ──────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_ReturnsContentInContext()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(content: "This is brand voice text.", label: "Markenstimme");

        var result = await provider.EnrichAsync("test briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("This is brand voice text.", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsSingleCitationWithLabel()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(content: "This is brand voice text.", label: "Markenstimme");

        var result = await provider.EnrichAsync("test briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(result.Citations);
        Assert.Equal("Markenstimme", result.Citations[0].Title);
    }

    [Fact]
    public async Task EnrichAsync_ZeroCreditsAndNullCostForContent()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(content: "content");

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.TokensOrCreditsUsed);
    }

    // ── EnrichAsync without content ───────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_EmptyContent_ReturnsEmptyContext()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(); // no content key

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(string.Empty, result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_EmptyContent_ReturnsNoCitations()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile();

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result.Citations);
    }

    // ── ConsultationId ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_SetsConsultationId_WhenContentProvided()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(content: "some content");

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(result.ConsultationId);
        Assert.NotEqual(Guid.Empty, result.ConsultationId!.Value);
    }

    [Fact]
    public async Task EnrichAsync_SetsConsultationId_WhenContentEmpty()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile();

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(result.ConsultationId);
        Assert.NotEqual(Guid.Empty, result.ConsultationId!.Value);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_PersistsGroundingConsultation()
    {
        var repo = new FakeGroundingConsultationRepository();
        var provider = new StaticContextGroundingProvider(CreateScopeFactory(repo));
        var profile = MakeProfile(content: "brand text", label: "Brand");
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("test briefing", profile, runId, CancellationToken.None);

        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal(runId, stored[0].RunId);
        Assert.Equal(profile.Name, stored[0].GroundingProviderName);
    }

    // ── Label fallback ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_WhenNoLabel_UsesFallbackTitle()
    {
        var provider = new StaticContextGroundingProvider(CreateScopeFactory());
        var profile = MakeProfile(content: "Content without label"); // no label key

        var result = await provider.EnrichAsync("briefing", profile, Guid.NewGuid(), CancellationToken.None);

        // Falls back to "Static Context" when label is missing
        Assert.Single(result.Citations);
        Assert.False(string.IsNullOrEmpty(result.Citations[0].Title));
    }

    // ── Fake repository ───────────────────────────────────────────────────────

    internal sealed class FakeGroundingConsultationRepository : IGroundingConsultationRepository
    {
        private readonly List<GroundingConsultation> _store = [];

        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>(
                _store.Where(c => c.RunId == runId).ToList());

        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct)
            => Task.CompletedTask;
    }
}
