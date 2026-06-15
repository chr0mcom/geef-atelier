namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>Well-known constants for composing a generic role prompt with specialization packs.</summary>
public static class PromptComposition
{
    /// <summary>
    /// Placeholder token in a generic role prompt at which the composed specialization layer is inserted.
    /// When absent, the layer is appended at the end under a heading (defined fallback).
    /// </summary>
    public const string SpecializationSlot = "{specialization}";

    /// <summary>Heading used when appending the specialization layer because the slot is absent.</summary>
    public const string FallbackHeading = "## Specialization";
}
