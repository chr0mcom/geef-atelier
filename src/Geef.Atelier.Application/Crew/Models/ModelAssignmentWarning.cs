namespace Geef.Atelier.Application.Crew.Models;

/// <summary>A model assignment that did not go as planned: a fallback model or a binding that breaks model plurality.</summary>
/// <param name="ActorName">Profile name of the affected actor.</param>
/// <param name="Detail">Technical reason produced by <see cref="ModelChooser"/> or the reviewer assignment; log only.</param>
/// <param name="ActorDisplayName">Display name of the affected actor; <c>null</c> falls back to <paramref name="ActorName"/>.</param>
public sealed record ModelAssignmentWarning(string ActorName, string Detail, string? ActorDisplayName = null)
{
    /// <summary>Name of the actor as the user sees it.</summary>
    public string ShownName => string.IsNullOrWhiteSpace(ActorDisplayName) ? ActorName : ActorDisplayName;
}
