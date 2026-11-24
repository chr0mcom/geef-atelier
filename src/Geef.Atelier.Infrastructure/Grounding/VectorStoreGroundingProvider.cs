using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider backed by the local vector-store knowledge base.
/// Embeds the briefing text, runs a similarity search, and surfaces matching chunks as citations.
/// The <c>Scope</c> setting decides what is searched:
/// <list type="bullet">
///   <item><c>global</c> (also when the setting is missing or blank): the shared documents only.</item>
///   <item><c>run-local</c>: the attachments of the run and of the runs it was resumed or composed from
///   (<see cref="IRunAttachmentLineage"/>), because attachments stay with the run they were uploaded to; when none of
///   them has an attachment, the provider returns an empty result without an embedding call.</item>
///   <item><c>both</c>: the shared documents plus the attachments of that same lineage.</item>
/// </list>
/// Attachments of other runs or users are never searched. Any other value is unknown: the provider falls back to the
/// shared documents (fail-closed) and records a run warning (<see cref="RunWarningCodes.GroundingScopeUnknown"/>).
/// </summary>
internal sealed class VectorStoreGroundingProvider(
    IEmbeddingProvider embeddingProvider,
    IServiceScopeFactory scopeFactory,
    IRunWarningSink warningSink,
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
        var scopeSetting = scopeStr.ToVectorStoreScope();
        if (scopeSetting is null)
        {
            logger.LogWarning(
                "Vector-store grounding: run={RunId} provider={Profile} has an unknown scope '{Scope}'; only shared documents are searched.",
                runId, profile.Name, scopeStr);
            await warningSink.ReportAsync(
                runId,
                RunWarningSources.Grounding,
                RunWarningCodes.GroundingScopeUnknown,
                profile.UnknownScopeWarning(),
                ct: ct);
            scopeSetting = VectorStoreScope.Global;
        }

        logger.LogInformation(
            "Vector-store grounding: run={RunId} provider={Profile} topK={TopK} tagFilter={Tags} scope={Scope}",
            runId, profile.Name, topK,
            tagFilter is null ? "(none)" : string.Join(",", tagFilter),
            scopeSetting);

        await using var scope = scopeFactory.CreateAsyncScope();
        var searchRepo = scope.ServiceProvider.GetRequiredService<IVectorSearchRepository>();

        var searches = new List<(KnowledgeScope Scope, Guid? RunId)>();
        if (scopeSetting is VectorStoreScope.Global or VectorStoreScope.Both)
            searches.Add((KnowledgeScope.Global, null));
        if (scopeSetting is VectorStoreScope.RunLocal or VectorStoreScope.Both)
        {
            var lineage = scope.ServiceProvider.GetRequiredService<IRunAttachmentLineage>();
            foreach (var attachmentRun in await lineage.RunsWithAttachmentsAsync(runId, ct))
                searches.Add((KnowledgeScope.RunLocal, attachmentRun));
            if (searches.Count == 0)
            {
                logger.LogInformation(
                    "Vector-store grounding: run={RunId} provider={Profile} has no attachments; skipped without search.",
                    runId, profile.Name);
                return new GroundingResult(profile.Name, string.Empty, [], 0, null);
            }
        }

        var embedding = await embeddingProvider.CreateAsync(briefingText, ct);

        var searchResults = new List<Core.Domain.Crew.Knowledge.VectorSearchResult>();
        foreach (var (scopeFilter, runIdFilter) in searches)
            searchResults.AddRange(await searchRepo.SearchAsync(embedding.Vector, topK, tagFilter, scopeFilter, runIdFilter, ct));
        if (searches.Count > 1)
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

/// <summary>What a vector-store profile searches, parsed from its <c>Scope</c> setting.</summary>
internal enum VectorStoreScope
{
    /// <summary>Shared documents only.</summary>
    Global,

    /// <summary>Attachments of the run and its lineage only.</summary>
    RunLocal,

    /// <summary>Shared documents plus the attachments of the run and its lineage.</summary>
    Both
}

internal static class VectorStoreScopeSettingExtensions
{
    /// <summary>
    /// Parses the <c>Scope</c> provider setting (case-insensitive, trimmed): missing or blank means
    /// <see cref="VectorStoreScope.Global"/>; <c>global</c>, <c>run-local</c> and <c>both</c> map to their scope;
    /// anything else returns <c>null</c>.
    /// </summary>
    public static VectorStoreScope? ToVectorStoreScope(this string? setting) =>
        string.IsNullOrWhiteSpace(setting)
            ? VectorStoreScope.Global
            : setting.Trim().ToLowerInvariant() switch
            {
                "global"    => VectorStoreScope.Global,
                "run-local" => VectorStoreScope.RunLocal,
                "both"      => VectorStoreScope.Both,
                _           => null
            };
}
