namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// The result of a single grounding refinement pass.
/// When <see cref="WasSkipped"/> is true, all citation and text fields reflect the unmodified provider output.
/// </summary>
/// <param name="RefinedCitations">Citations that survived the refinement pass (or the full set when skipped/synthesized).</param>
/// <param name="DroppedCitations">Citations removed by the refiner, with stated reasons. Empty when skipped or in Synthesize mode.</param>
/// <param name="SynthesizedText">LLM-generated summary text produced in <see cref="GroundingRefinementMode.Synthesize"/> mode; <c>null</c> in Filter mode.</param>
/// <param name="WasSkipped">True when refinement was configured but bypassed (e.g. no citations, missing config, or explicit skip).</param>
/// <param name="SkipReason">Human-readable explanation for why refinement was skipped; <c>null</c> when <see cref="WasSkipped"/> is false.</param>
public sealed record RefinementOutcome(
    IReadOnlyList<SourceCitation> RefinedCitations,
    IReadOnlyList<DroppedCitation> DroppedCitations,
    string? SynthesizedText,
    bool WasSkipped,
    string? SkipReason);
