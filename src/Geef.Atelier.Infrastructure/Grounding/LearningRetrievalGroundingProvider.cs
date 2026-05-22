using System.Text;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Retrieves domain-boosted approved learnings from the learning store using cosine similarity.
/// Same-domain learnings are boosted; cross-domain learnings are penalised.
/// </summary>
internal sealed class LearningRetrievalGroundingProvider(
    IEmbeddingProvider embeddingProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<LearningRetrievalGroundingProvider> logger) : IGroundingProvider
{
    public string ProviderType => GroundingProviderTypes.LearningRetrieval;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var sameDomainBoost    = ParseDouble(profile.ProviderSettings, "sameDomainBoost",    1.0);
        var crossDomainPenalty = ParseDouble(profile.ProviderSettings, "crossDomainPenalty", 0.5);
        var maxLearnings       = ParseInt   (profile.ProviderSettings, "maxLearnings",       4);

        await using var scope = scopeFactory.CreateAsyncScope();
        var runRepo      = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var learningRepo = scope.ServiceProvider.GetRequiredService<ILearningRepository>();
        var consultRepo  = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();

        // Determine current run's domain from its crew template name.
        var run = await runRepo.GetByIdAsync(runId, ct);
        var currentDomain = run?.CrewTemplateName;

        var embedding = await embeddingProvider.CreateAsync(briefingText, ct);

        var matches = await learningRepo.SearchApprovedAsync(
            embedding.Vector,
            currentDomain,
            sameDomainBoost,
            crossDomainPenalty,
            maxLearnings,
            ct);

        logger.LogInformation(
            "Learning-retrieval grounding: run={RunId} domain={Domain} topK={TopK} found={Count}",
            runId, currentDomain ?? "(none)", maxLearnings, matches.Count);

        var citations = matches
            .Select(m => new SourceCitation(
                Title:             $"Learning ({m.Entry.Domain})",
                Url:               null,
                Snippet:           Truncate(m.Entry.Text, 300),
                DocumentReference: $"learning://{m.Entry.Id}",
                RelevanceScore:    m.Similarity))
            .ToList();

        var enrichedContext = BuildEnrichedContext(citations, matches);

        var consultation = new GroundingConsultation(
            Id:                   Guid.NewGuid(),
            RunId:                runId,
            GroundingProviderName: profile.Name,
            Query:                briefingText,
            Citations:            citations,
            TokensOrCreditsUsed:  embedding.TokenCount,
            CostEur:              embedding.CostEur,
            CreatedAt:            DateTimeOffset.UtcNow);

        await consultRepo.CreateAsync(consultation, ct);

        return new GroundingResult(
            ProviderName:        profile.Name,
            EnrichedContext:     enrichedContext,
            Citations:           citations,
            TokensOrCreditsUsed: embedding.TokenCount,
            CostEur:             embedding.CostEur,
            ConsultationId:      consultation.Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildEnrichedContext(
        IReadOnlyList<SourceCitation> citations,
        IReadOnlyList<(LearningEntry Entry, double Similarity)> matches)
    {
        if (citations.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Learnings from Previous Runs");
        sb.AppendLine();
        for (var i = 0; i < citations.Count; i++)
        {
            var entry = matches[i].Entry;
            var score = matches[i].Similarity;
            sb.AppendLine($"### [{i + 1}] {entry.Domain} (score: {score:F3})");
            sb.AppendLine();
            sb.AppendLine(entry.Text);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static double ParseDouble(Dictionary<string, string> s, string key, double def) =>
        s.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : def;

    private static int ParseInt(Dictionary<string, string> s, string key, int def) =>
        s.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
