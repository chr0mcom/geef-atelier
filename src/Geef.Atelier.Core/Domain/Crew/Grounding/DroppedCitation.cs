namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// A citation that was removed during a refinement pass, together with the model's stated reason.
/// Retained for transparency and audit logging.
/// </summary>
/// <param name="Original">The citation that was dropped.</param>
/// <param name="Reason">Short explanation from the refiner LLM for why this citation was excluded.</param>
public sealed record DroppedCitation(SourceCitation Original, string Reason);
