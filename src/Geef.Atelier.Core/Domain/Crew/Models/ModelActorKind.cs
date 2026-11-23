namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Kind of persisted profile that can carry a <see cref="ModelSlot"/>.</summary>
public enum ModelActorKind
{
    /// <summary>Executor profile.</summary>
    Executor = 0,

    /// <summary>Reviewer profile.</summary>
    Reviewer = 1,

    /// <summary>Advisor profile.</summary>
    Advisor = 2,
}
