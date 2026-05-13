namespace Geef.Atelier.Core.Domain.Crew.Advisors;

/// <summary>
/// Behavioural archetype for an advisor. Determines how the advisor's response is integrated into
/// the pipeline (grounding hints vs. critique vs. devil's-advocate prompts vs. domain-expert input).
/// Stub for PS-7 — no functional advisor pass is wired in PS-5.
/// </summary>
public enum AdvisorMode
{
    /// <summary>Strategic advisor: high-level direction and framing input during grounding.</summary>
    Strategic = 0,

    /// <summary>Critical advisor: rigorous critique of the draft prior to finalisation.</summary>
    Critical = 1,

    /// <summary>Devil's-advocate advisor: actively challenges core assumptions in the draft.</summary>
    DevilsAdvocate = 2,

    /// <summary>Domain expert: subject-matter input on factual or terminological questions.</summary>
    DomainExpert = 3,
}
