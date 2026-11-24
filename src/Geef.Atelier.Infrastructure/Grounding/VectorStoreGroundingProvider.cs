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
/// provider returns an empty result without an embedding call.
/// </summary>
internal sealed class VectorStoreGroundingProvider(
    IEmbeddingProvider embeddingProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<VectorStoreGroundingProvider> logger) : IGroundingProvider
{
    private const int MaxLineageRuns = 10;

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

        logger.LogInformation(
            "Vector-store grounding: run={RunId} provider={Profile} topK={TopK} tagFilter={Tags} scope={Scope}",
            runId, profile.Name, topK,
            tagFilter is null ? "(none)" : string.Join(",", tagFilter),
            scopeFilter?.ToString() ?? "(none)");

        await using var scope = scopeFactory.CreateAsyncScope();
        var searchRepo = scope.ServiceProvider.GetRequiredService<IVectorSearchRepository>();

        IReadOnlyList<Guid?> runFilters = [null];
        if (scopeFilter == KnowledgeScope.RunLocal)
        {
            runFilters = await AttachmentRunIdsAsync(scope.ServiceProvider, runId, ct);
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

    /// <summary>
    /// Run ids whose attachments belong to <paramref name="runId"/>: the run itself and, following resume
    /// (<c>ParentRunId</c>) and auto-composition (<c>ParentCompositionRunId</c>) links, its ancestors — only those
    /// that actually have attachments. Without the document repository (isolated tests) the run itself is searched.
    /// </summary>
    private static async Task<IReadOnlyList<Guid?>> AttachmentRunIdsAsync(IServiceProvider services, Guid runId, CancellationToken ct)
    {
        var documents = services.GetService<IKnowledgeDocumentRepository>();
        var runs = services.GetService<IRunRepository>();
        if (documents is null || runs is null)
            return [runId];

        var result = new List<Guid?>();
        var visited = new HashSet<Guid>();
        var pending = new Queue<Guid>([runId]);
        while (pending.Count > 0 && visited.Count < MaxLineageRuns)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current))
                continue;

            if ((await documents.ListByRunAsync(current, ct)).Count > 0)
                result.Add(current);

            var run = await runs.GetByIdAsync(current, ct);
            if (run?.ParentRunId is { } parent)
                pending.Enqueue(parent);
            if (run?.ParentCompositionRunId is { } composer)
                pending.Enqueue(composer);
        }

        return result;
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
