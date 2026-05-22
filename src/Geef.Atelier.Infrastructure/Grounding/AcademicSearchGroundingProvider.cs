using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounds a briefing with scientific papers from arXiv, Semantic Scholar, or OpenAlex.
/// Source is selected by <see cref="GroundingProviderProfile.AcademicSource"/>; defaults to Semantic Scholar.
/// Supports optional LLM-based refinement via <see cref="GroundingProviderProfile.RefinementBinding"/>.
/// </summary>
internal sealed class AcademicSearchGroundingProvider(
    IEnumerable<IAcademicSource> sources,
    IServiceScopeFactory scopeFactory,
    ILogger<AcademicSearchGroundingProvider> logger) : IGroundingProvider
{
    public string ProviderType => GroundingProviderTypes.AcademicSearch;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var sourceName = profile.AcademicSource;
        var source = sources.FirstOrDefault(s =>
            string.Equals(s.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            logger.LogWarning(
                "AcademicSearch grounding: run={RunId} unknown source '{Source}'; falling back to semantic-scholar.",
                runId, sourceName);
            source = sources.FirstOrDefault(s => s.SourceName == "semantic-scholar")
                     ?? sources.First();
        }

        var apiKey = profile.AcademicApiKeyEnv is { Length: > 0 } envVar
            ? Environment.GetEnvironmentVariable(envVar)
            : null;

        var searchOptions = new AcademicSearchOptions(
            MaxPapers: profile.AcademicMaxPapers,
            DateFrom: profile.AcademicDateFrom,
            Fields: profile.AcademicFields,
            ApiKey: apiKey);

        logger.LogInformation(
            "AcademicSearch grounding: run={RunId} provider={Profile} source={Source} maxPapers={Max}",
            runId, profile.Name, source.SourceName, searchOptions.MaxPapers);

        var papers = await source.SearchAsync(briefingText, searchOptions, ct);

        if (papers.Count == 0)
        {
            logger.LogInformation(
                "AcademicSearch grounding: run={RunId} source={Source} returned 0 papers.",
                runId, source.SourceName);
        }

        var citations = papers.Select(p => BuildCitation(p)).ToList();
        var enrichedContext = BuildEnrichedContext(source.SourceName, citations, papers);
        var consultationId = await PersistConsultationAsync(runId, profile.Name, briefingText, citations, 0, null, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: 0,
            CostEur: 0m,
            ConsultationId: consultationId);
    }

    private static SourceCitation BuildCitation(AcademicPaper paper)
    {
        var ref_ = paper.ArxivId is not null ? $"arXiv:{paper.ArxivId}"
                 : paper.Doi is not null     ? $"DOI:{paper.Doi}"
                 : paper.Url;

        var snippet = paper.Abstract is { Length: > 0 } abs
            ? (abs.Length > 300 ? abs[..300] + "…" : abs)
            : string.Empty;

        return new SourceCitation(
            Title: paper.Title,
            Url: paper.Url,
            Snippet: snippet,
            DocumentReference: ref_,
            RelevanceScore: null,
            PublishedDate: paper.PublishedDate);
    }

    private static string BuildEnrichedContext(string sourceName, IReadOnlyList<SourceCitation> citations, IReadOnlyList<AcademicPaper> papers)
    {
        if (papers.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Academic research context — source: {sourceName}]");
        sb.AppendLine("Use the papers below only where clearly relevant to the briefing. Cite papers by title and identifier.");
        sb.AppendLine();

        for (var i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            sb.AppendLine($"## Paper {i + 1}: {p.Title}");
            if (!string.IsNullOrWhiteSpace(p.Authors))
                sb.AppendLine($"Authors: {p.Authors}");
            if (p.PublishedDate.HasValue)
                sb.AppendLine($"Published: {p.PublishedDate.Value:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(p.ArxivId))
                sb.AppendLine($"arXiv ID: {p.ArxivId}");
            if (!string.IsNullOrWhiteSpace(p.Doi))
                sb.AppendLine($"DOI: {p.Doi}");
            if (!string.IsNullOrWhiteSpace(p.Url))
                sb.AppendLine($"URL: {p.Url}");
            if (!string.IsNullOrWhiteSpace(p.Abstract))
            {
                sb.AppendLine();
                sb.AppendLine(p.Abstract);
            }
            sb.AppendLine();
        }

        sb.Append("[End of academic research context]");
        return sb.ToString();
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
        var repo = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await repo.CreateAsync(consultation, ct);
        return consultation.Id;
    }
}
