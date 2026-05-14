using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Application contract for the Template Studio — meta-LLM analysis and materialization.</summary>
public interface ITemplateStudioService
{
    /// <summary>
    /// Analyses the task description via a meta-LLM call and returns a structured proposal.
    /// The analysis is persisted as an audit record regardless of whether the user materialises it.
    /// </summary>
    Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default);

    /// <summary>
    /// Persists the user-confirmed (and possibly edited) proposal as custom crew records.
    /// Returns the names of all created records and any provider-availability warnings.
    /// </summary>
    Task<MaterializationResult> MaterializeAsync(
        Guid analysisId,
        MaterializationRequest request,
        CancellationToken ct = default);

    /// <summary>Returns a paginated list of analysis history entries, ordered by creation date descending.</summary>
    Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Paginated result of studio analysis history queries.</summary>
public sealed record StudioAnalysesPage(
    IReadOnlyList<StudioAnalysisHistoryEntry> Items,
    bool HasMore);

/// <summary>Summary entry for the analysis history list.</summary>
public sealed record StudioAnalysisHistoryEntry(
    Guid Id,
    string TaskDescription,
    string ReasoningSummary,
    string? MaterializedTemplateName,
    decimal? CostEur,
    DateTimeOffset CreatedAt);
