using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Application.Grounding;

/// <summary>
/// Applies an LLM-powered refinement pass to a raw grounding result, either filtering
/// irrelevant citations or synthesizing a coherent summary across all sources.
/// </summary>
public interface IGroundingRefiner
{
    /// <summary>
    /// Refines the raw grounding result according to the given configuration.
    /// Always returns a valid pair — never throws. Callers must inspect
    /// <see cref="RefinementOutcome.WasSkipped"/> to detect degraded paths.
    /// </summary>
    /// <param name="raw">The unmodified result from the grounding provider.</param>
    /// <param name="briefing">The user briefing, used to judge source relevance.</param>
    /// <param name="config">Refinement mode, LLM binding, and optional custom instructions.</param>
    /// <param name="groundingProviderName">Profile name of the provider that produced <paramref name="raw"/>; used for cost attribution.</param>
    /// <param name="runId">The current run identifier, stored with cost records.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(GroundingResult Refined, RefinementOutcome Outcome)> RefineAsync(
        GroundingResult raw,
        string briefing,
        GroundingRefinementConfig config,
        string groundingProviderName,
        Guid runId,
        CancellationToken ct);
}
