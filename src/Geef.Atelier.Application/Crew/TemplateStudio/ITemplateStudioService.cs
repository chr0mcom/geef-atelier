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
}
