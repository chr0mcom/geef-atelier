using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Grounding;

internal sealed class StaticContextGroundingProvider(
    IServiceScopeFactory scopeFactory) : IGroundingProvider
{
    public string ProviderType => GroundingProviderTypes.StaticContext;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var content = profile.StaticContent;

        if (string.IsNullOrWhiteSpace(content))
        {
            var emptyId = await PersistConsultationAsync(runId, profile.Name, briefingText, [], 0, null, ct);
            return new GroundingResult(profile.Name, string.Empty, [], 0, null, emptyId);
        }

        var label = profile.StaticLabel ?? "Static Context";

        var citation = new SourceCitation(
            Title: label,
            Url: null,
            Snippet: content.Length <= 300 ? content : content[..300],
            DocumentReference: $"static://{profile.StaticLabel ?? "context"}",
            RelevanceScore: null,
            PublishedDate: null);

        var citations = new List<SourceCitation> { citation };
        var enrichedContext = $"## {label}\n\n{content}";

        var consultationId = await PersistConsultationAsync(runId, profile.Name, briefingText, citations, 0, null, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: 0,
            CostEur: 0m,
            ConsultationId: consultationId);
    }

    private async Task<Guid> PersistConsultationAsync(
        Guid runId,
        string providerName,
        string query,
        IReadOnlyList<SourceCitation> citations,
        int tokensOrCredits,
        decimal? costEur,
        CancellationToken ct)
    {
        var consultation = new GroundingConsultation(
            Id:                    Guid.NewGuid(),
            RunId:                 runId,
            GroundingProviderName: providerName,
            Query:                 query,
            Citations:             citations,
            TokensOrCreditsUsed:   tokensOrCredits,
            CostEur:               costEur,
            CreatedAt:             DateTimeOffset.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var consultationRepository = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await consultationRepository.CreateAsync(consultation, ct);
        return consultation.Id;
    }
}
