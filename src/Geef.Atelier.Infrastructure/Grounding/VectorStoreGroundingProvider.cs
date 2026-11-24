using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider backed by the local vector-store knowledge base.
/// Embeds the briefing text, runs a similarity search, and surfaces matching chunks as citations.
/// For the run-local scope (attachments) the search covers the run and the runs it was resumed or composed from,
/// because attachments stay with the run they were uploaded to; when none of them has an attachment, the
/// provider returns an empty result without an embedding call. A missing <c>Scope</c> setting means the shared
/// (global) documents; an unknown value is treated the same way and logged, so a profile can never reach the
/// attachments of another user's run.
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

        profile.ProviderSettings.TryGetValue("Scope", out var scopeStr);
        var scopeFilter = scopeStr.ToKnowledgeScope();
        if (scopeFilter is null)
        {
            logger.LogWarning(
                "Vector-store grounding: run={RunId} provider={Profile} has an unknown scope '{Scope}'; only shared documents are searched.",
                runId, profile.Name, scopeStr);
            scopeFilter = KnowledgeScope.Global;
        }

        logger.LogInformation(
            "Vector-store grounding: run={RunId} provider={Profile} topK={TopK} tagFilter={Tags} scope={Scope}",
            runId, profile.Name, topK,
            tagFilter is null ? "(none)" : string.Join(",", tagFilter),
            scopeFilter);

        await using var scope = scopeFactory.CreateAsyncScope();
        var searchRepo = scope.ServiceProvider.GetRequiredService<IVectorSearchRepository>();

        IReadOnlyList<Guid?> runFilters = [null];
        if (scopeFilter == KnowledgeScope.RunLocal)
        {
            var lineage = scope.ServiceProvider.GetRequiredService<IRunAttachmentLineage>();
            runFilters = (await lineage.RunsWithAttachmentsAsync(runId, ct)).Select(id => (Guid?)id).ToList();
            if (runFilters.Count == 0)
            {
                logger.LogInformation(
                    "Vector-store grounding: run={RunId} provider={Profile} has no attachments; skipped without search.",
                    runId, profile.Name);
                return new GroundingResult(profile.Name, string.Empty, [], 0, null);
            }
        }

        var embedding = await embeddingProvider.CreateAsync(briefingText, ct);

        var searchResults = new List<Core.Domain.Crew.Knowledge.VectorSearchResult>();
        foreach (var runIdFilter in runFilters)
            searchResults.AddRange(await searchRepo.SearchAsync(embedding.Vector, topK, tagFilter, scopeFilter, runIdFilter, ct));
        if (runFilters.Count > 1)
            searchResults = searchResults.OrderByDescending(r => r.Similarity).Take(topK).ToList();

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
            CostEur: embedding.CostEur,
            ConsultationId: consultation.Id);
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

internal static class KnowledgeScopeSettingExtensions
{
    /// <summary>
    /// Parses the <c>Scope</c> provider setting: missing or blank means <see cref="KnowledgeScope.Global"/>,
    /// <c>run-local</c> and <c>global</c> map to their scope, anything else returns <c>null</c>.
    /// </summary>
    public static KnowledgeScope? ToKnowledgeScope(this string? setting) =>
        string.IsNullOrWhiteSpace(setting)
            ? KnowledgeScope.Global
            : setting.Trim().ToLowerInvariant() switch
            {
                "run-local" => KnowledgeScope.RunLocal,
                "global"    => KnowledgeScope.Global,
                _           => null
            };
}
