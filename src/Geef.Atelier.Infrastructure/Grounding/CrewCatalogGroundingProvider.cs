using System.Text;
using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider that embeds the briefing text, queries existing crew templates and profiles
/// by pgvector cosine similarity with optional domain boosting, and returns them as structured
/// grounding context. Falls back to a static catalog listing when no embeddings have been
/// computed yet (e.g. on a fresh installation).
/// </summary>
/// <remarks>
/// Registered as <c>Singleton</c>. All scoped dependencies (<see cref="ICrewService"/>,
/// <see cref="ICrewTemplateEmbeddingRepository"/>, <see cref="IGroundingConsultationRepository"/>)
/// are resolved per invocation via <see cref="IServiceScopeFactory"/> to avoid captive-dependency
/// issues.
/// </remarks>
internal sealed class CrewCatalogGroundingProvider(
    IEmbeddingProvider embeddingProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<CrewCatalogGroundingProvider> logger) : IGroundingProvider
{
    /// <inheritdoc/>
    public string ProviderType => GroundingProviderTypes.CrewCatalog;

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var topK            = ParseInt   (profile.ProviderSettings, "topK",           6);
        var sameDomainBoost = ParseDouble(profile.ProviderSettings, "sameDomainBoost", 1.5);
        var domainHint      = profile.ProviderSettings.TryGetValue("domain", out var d)
                              && !string.IsNullOrWhiteSpace(d) ? d : null;

        await using var scope         = scopeFactory.CreateAsyncScope();
        var embeddingRepo             = scope.ServiceProvider.GetRequiredService<ICrewTemplateEmbeddingRepository>();
        var crewService               = scope.ServiceProvider.GetRequiredService<ICrewService>();
        var consultRepo               = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();

        // 1. Embed the briefing.
        var embeddingResult = await embeddingProvider.CreateAsync(briefingText, ct);

        // 2. Search for similar crew templates by vector similarity.
        var matches = await embeddingRepo.SearchAsync(
            embeddingResult.Vector,
            domainHint,
            sameDomainBoost,
            topK,
            ct);

        logger.LogInformation(
            "CrewCatalog grounding: run={RunId} domainHint={Domain} topK={TopK} matched={Count}",
            runId, domainHint ?? "(none)", topK, matches.Count);

        // 3. Build citations for matched templates.
        var citations = matches
            .Select(m => new SourceCitation(
                Title:             $"Crew Template: {m.Entry.TemplateName} ({m.Entry.Domain})",
                Url:               null,
                Snippet:           Truncate(m.Entry.Summary, 300),
                DocumentReference: $"crew-template://{m.Entry.TemplateName}",
                RelevanceScore:    m.Similarity))
            .ToList();

        // 4. Build the enriched context markdown.
        var enrichedContext = await BuildEnrichedContextAsync(matches, crewService, ct);

        // 5. Persist the grounding consultation.
        var consultationId = await PersistConsultationAsync(
            runId, profile.Name, briefingText, citations,
            embeddingResult.TokenCount, embeddingResult.CostEur,
            consultRepo, ct);

        return new GroundingResult(
            ProviderName:        profile.Name,
            EnrichedContext:     enrichedContext,
            Citations:           citations,
            TokensOrCreditsUsed: embeddingResult.TokenCount,
            CostEur:             embeddingResult.CostEur,
            ConsultationId:      consultationId);
    }

    // ── Context builder ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the markdown grounding block. When no embeddings exist yet (fresh installation)
    /// the method falls back to a static summary listing all crew templates and the top profiles
    /// by type (reviewer, executor).
    /// </summary>
    private static async Task<string> BuildEnrichedContextAsync(
        IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)> matches,
        ICrewService crewService,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Crew Catalog");
        sb.AppendLine();

        if (matches.Count > 0)
        {
            sb.AppendLine("### Similar Crew Templates (by semantic similarity)");
            sb.AppendLine();
            for (var i = 0; i < matches.Count; i++)
            {
                var entry = matches[i].Entry;
                var score = matches[i].Similarity;
                sb.AppendLine($"#### [{i + 1}] `{entry.TemplateName}` — domain: {entry.Domain} (score: {score:F3})");
                sb.AppendLine();
                sb.AppendLine(entry.Summary);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }
        else
        {
            // Fallback: no embeddings yet → list all templates + top profiles statically.
            sb.AppendLine("_No crew template embeddings found. Listing available catalog entries:_");
            sb.AppendLine();

            await AppendStaticCatalogAsync(sb, crewService, ct);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Static fallback: appends all crew templates and a representative sample of each
    /// profile type (reviewer, executor) from the catalog. Capped at 10 per type.
    /// </summary>
    private static async Task AppendStaticCatalogAsync(
        StringBuilder sb,
        ICrewService crewService,
        CancellationToken ct)
    {
        const int cap = 10;

        // Crew templates
        var templates = await crewService.ListCrewTemplatesAsync(includeSystem: true, cancellationToken: ct);
        if (templates.Count > 0)
        {
            sb.AppendLine("### Available Crew Templates");
            sb.AppendLine();
            foreach (var t in templates)
            {
                sb.AppendLine($"- **{t.Name}** ({t.DisplayName}): {t.Description}");
            }
            sb.AppendLine();
        }

        // Reviewer profiles
        var reviewers = await crewService.ListReviewerProfilesAsync(includeSystem: true, cancellationToken: ct);
        if (reviewers.Count > 0)
        {
            sb.AppendLine("### Available Reviewer Profiles (sample)");
            sb.AppendLine();
            foreach (var r in reviewers.Take(cap))
            {
                sb.AppendLine($"- **{r.Name}** ({r.DisplayName}): {r.Description}");
            }
            sb.AppendLine();
        }

        // Executor profiles
        var executors = await crewService.ListExecutorProfilesAsync(includeSystem: true, cancellationToken: ct);
        if (executors.Count > 0)
        {
            sb.AppendLine("### Available Executor Profiles (sample)");
            sb.AppendLine();
            foreach (var e in executors.Take(cap))
            {
                sb.AppendLine($"- **{e.Name}** ({e.DisplayName}): {e.Description}");
            }
            sb.AppendLine();
        }
    }

    // ── Consultation persistence ─────────────────────────────────────────────

    private static async Task<Guid> PersistConsultationAsync(
        Guid runId,
        string providerName,
        string query,
        IReadOnlyList<SourceCitation> citations,
        int tokensOrCredits,
        decimal? costEur,
        IGroundingConsultationRepository consultRepo,
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

        await consultRepo.CreateAsync(consultation, ct);
        return consultation.Id;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static double ParseDouble(Dictionary<string, string> s, string key, double def) =>
        s.TryGetValue(key, out var v) && double.TryParse(v,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : def;

    private static int ParseInt(Dictionary<string, string> s, string key, int def) =>
        s.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
