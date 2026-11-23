namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Subject area a crew works in; selects the matrix row together with <see cref="ModelSlot"/>.
/// Explicitly numbered and append-only, because snapshots serialize enums as numbers.
/// </summary>
public enum ModelDomain
{
    /// <summary>No specific domain.</summary>
    General = 0,

    /// <summary>Business letters and e-mails.</summary>
    Correspondence = 1,

    /// <summary>Legal texts and argumentation.</summary>
    Legal = 2,

    /// <summary>Academic and scientific texts, grant proposals.</summary>
    Academic = 3,

    /// <summary>Marketing copy and social media posts.</summary>
    Marketing = 4,

    /// <summary>Speeches and rhetoric.</summary>
    Speech = 5,

    /// <summary>Technical and analytical texts, software and architecture.</summary>
    Technical = 6,
}
