using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider backed by the local vector-store knowledge base.
/// Embeds the briefing text, runs a similarity search, and surfaces matching chunks as citations.
/// </summary>
internal sealed class VectorStoreGroundingProvider(
    IEmbeddingProvider embeddingProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<VectorStoreGroundingProvider> logger) : IGroundingProvider
{
    public string ProviderType => "vector-store";

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        profile.ProviderSettings.TryGetValue("TopK", out var topKStr);
        var topK = int.TryParse(topKStr, out var tk) ? tk : 5;

        IReadOnlyList<string>? tagFilter = null;
        if (profile.ProviderSettings.TryGetValue("TagFilter", out var tagFilterStr)
            && !string.IsNullOrWhiteSpace(tagFilterStr))
        {
            tagFilter = tagFilterStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        KnowledgeScope? scopeFilter = null;
        if (profile.ProviderSettings.TryGetValue("Scope", out var scopeStr)
            && !string.IsNullOrWhiteSpace(scopeStr))
        {
            scopeFilter = scopeStr.Trim().ToLowerInvariant() switch
            {
                "run-local" => KnowledgeScope.RunLocal,
                "global"    => KnowledgeScope.Global,
                _           => null
            };
        }

        Guid? runIdFilter = scopeFilter == KnowledgeScope.RunLocal ? runId : null;

        logger.LogInformation(
            "Vector-store grounding: run={RunId} provider={Profile} topK={TopK} tagFilter={Tags} scope={Scope}",
            runId, profile.Name, topK,
            tagFilter is null ? "(none)" : string.Join(",", tagFilter),
            scopeFilter?.ToString() ?? "(none)");

        var embedding = await embeddingProvider.CreateAsync(briefingText, ct);

        await using var scope = scopeFactory.CreateAsyncScope();
        var searchRepo = scope.ServiceProvider.GetRequiredService<IVectorSearchRepository>();

        var searchResults = await searchRepo.SearchAsync(embedding.Vector, topK, tagFilter, scopeFilter, runIdFilter, ct);

        var citations = searchResults
            .Select(r => new SourceCitation(
                Title: r.DocumentTitle,
                Url: null,
                Snippet: Truncate(r.Chunk.Content, 300),
                DocumentReference: $"{r.Chunk.DocumentId}/chunk-{r.Chunk.ChunkIndex}",
                RelevanceScore: r.Similarity))
            .ToList();

        var enrichedContext = BuildEnrichedContext(citations, searchResults);

        var consultation = new GroundingConsultation(
            Id: Guid.NewGuid(),
            RunId: runId,
            GroundingProviderName: profile.Name,
            Query: briefingText,
            Citations: citations,
            TokensOrCreditsUsed: embedding.TokenCount,
            CostEur: embedding.CostEur,
            CreatedAt: DateTimeOffset.UtcNow);

        var consultationRepo = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await consultationRepo.CreateAsync(consultation, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: embedding.TokenCount,
            CostEur: embedding.CostEur);
    }

    private static string BuildEnrichedContext(
        IReadOnlyList<SourceCitation> citations,
        IReadOnlyList<Core.Domain.Crew.Knowledge.VectorSearchResult> results)
    {
        if (citations.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Knowledge Base Results");
        sb.AppendLine();
        for (var i = 0; i < citations.Count; i++)
        {
            var c = citations[i];
            var similarity = results[i].Similarity;
            sb.AppendLine($"### [{i + 1}] {c.Title}");
            sb.AppendLine($"*Relevance: {similarity:F3}*");
            sb.AppendLine();
            sb.AppendLine(c.Snippet);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
