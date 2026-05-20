namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// Determines how the grounding refiner processes citations after the raw provider result is received.
/// </summary>
public enum GroundingRefinementMode
{
    /// <summary>Irrelevant citations are removed; the remaining citations are passed through unchanged.</summary>
    Filter = 0,

    /// <summary>Citations are distilled into a new synthesized text block by the LLM; all citations are retained.</summary>
    Synthesize = 1,
}
