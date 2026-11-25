namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Role an LLM-backed actor plays in a crew. Together with the crew's <see cref="ModelDomain"/> it selects the
/// model from <see cref="ModelMatrix"/>. Values are persisted as names and serialized as numbers in snapshots,
/// so members are explicitly numbered and only ever appended.
/// </summary>
public enum ModelSlot
{
    /// <summary>Drafts the text.</summary>
    Executor = 0,

    /// <summary>Reviews facts, logic, sources and completeness.</summary>
    FactReviewer = 1,

    /// <summary>Reviews language, register, tone and structure.</summary>
    StyleReviewer = 2,

    /// <summary>Reviews subject-matter substance, strategy and counter-positions.</summary>
    DomainReviewer = 3,

    /// <summary>Strategic or pre-mortem advisor.</summary>
    StrategicAdvisor = 4,

    /// <summary>Critical advisor or devil's advocate.</summary>
    CriticalAdvisor = 5,

    /// <summary>Subject-matter expert advisor.</summary>
    DomainExpertAdvisor = 6,

    /// <summary>LLM that filters and condenses grounding results.</summary>
    GroundingRefiner = 7,

    /// <summary>Transform finalizer that rewrites the text (voice, tone).</summary>
    TransformRewrite = 8,

    /// <summary>Transform finalizer that condenses the text (summary, takeaways, glossary).</summary>
    TransformCondense = 9,

    /// <summary>Executor of the crew-composer system crew.</summary>
    Composer = 10,

    /// <summary>Lightweight filtering or classification helper.</summary>
    Helper = 11,

    /// <summary>Prepares an order before writing: what is missing and what to ask.</summary>
    Intake = 12,
}
