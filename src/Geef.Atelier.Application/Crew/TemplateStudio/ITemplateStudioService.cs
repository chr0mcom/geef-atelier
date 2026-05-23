using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Application contract for the Template Studio — meta-LLM analysis and materialization.</summary>
public interface ITemplateStudioService
{
    /// <summary>
    /// Analyses the task description via a meta-LLM call and returns a structured proposal.
    /// The analysis is persisted as an audit record regardless of whether the user materialises it.
    /// Uses the effective default provider/model (persisted default, else appsettings).
    /// </summary>
    Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default);

    /// <summary>
    /// Analyses the task description using an explicit provider/model override for this single call.
    /// When <paramref name="overrideChoice"/> is null or incomplete, falls back to the effective default.
    /// </summary>
    Task<TemplateStudioAnalysis> AnalyzeAsync(
        string taskDescription, StudioModelChoice? overrideChoice, CancellationToken ct = default);

    /// <summary>
    /// Analyses the task description, reporting human-readable phase updates via <paramref name="progress"/>
    /// (e.g. "Sammle Modelle…", "Frage Meta-KI an…") so the UI can show live status instead of an opaque spinner.
    /// </summary>
    Task<TemplateStudioAnalysis> AnalyzeAsync(
        string taskDescription, StudioModelChoice? overrideChoice, IProgress<string>? progress, CancellationToken ct = default);

    /// <summary>
    /// Returns the provider/model/max-tokens that an analysis would actually use right now:
    /// the persisted default when set, otherwise the appsettings default.
    /// </summary>
    Task<StudioModelChoice> GetEffectiveDefaultAsync(CancellationToken ct = default);

    /// <summary>Persists <paramref name="choice"/> as the new default for future analyses.</summary>
    Task SaveDefaultAsync(StudioModelChoice choice, CancellationToken ct = default);

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

/// <summary>A provider + model + max-tokens selection for a Template Studio meta-LLM call.</summary>
public sealed record StudioModelChoice(string Provider, string Model, int MaxTokens);

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
