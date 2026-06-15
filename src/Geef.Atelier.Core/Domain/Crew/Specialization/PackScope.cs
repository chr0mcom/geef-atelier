namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>
/// Reuse scope of a <see cref="SpecializationPack"/>. Determines in which crews a pack may be bound.
/// </summary>
public enum PackScope
{
    /// <summary>Reusable in any crew (e.g. <c>concise-output</c>, <c>executive-tone</c>).</summary>
    General = 0,

    /// <summary>Reusable only in crews of the same <see cref="SpecializationPack.Domain"/> (e.g. <c>legal-terminology</c>).</summary>
    DomainScoped = 1,

    /// <summary>
    /// Bound to a single owning crew (<see cref="SpecializationPack.OwningCrewId"/>); cascade-deleted with it.
    /// Never visible to other crews.
    /// </summary>
    TaskBound = 2
}
