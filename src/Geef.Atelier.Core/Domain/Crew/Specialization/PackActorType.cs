namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>
/// Actor type a <see cref="SpecializationPack"/> may be bound to. A pack declares the actor types
/// it applies to so that pickers and the composer only offer type-compatible packs.
/// </summary>
public enum PackActorType
{
    /// <summary>Applies to every actor type.</summary>
    Any = 0,

    /// <summary>The drafting executor.</summary>
    Executor = 1,

    /// <summary>A reviewer.</summary>
    Reviewer = 2,

    /// <summary>An advisor.</summary>
    Advisor = 3,

    /// <summary>The LLM-driven parts of a grounding provider (refiner / query shaping).</summary>
    Grounding = 4,

    /// <summary>An LLM-driven (<c>Transform</c>) finalizer.</summary>
    Finalizer = 5
}

/// <summary>Helpers for <see cref="PackActorType"/>.</summary>
public static class PackActorTypeExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when a pack declaring <paramref name="applicable"/> actor types
    /// may be bound to an actor of type <paramref name="target"/>. <see cref="PackActorType.Any"/> in the
    /// declared set matches every target.
    /// </summary>
    public static bool AppliesTo(this IReadOnlyList<PackActorType> applicable, PackActorType target) =>
        applicable.Count == 0
        || applicable.Contains(PackActorType.Any)
        || applicable.Contains(target);
}
