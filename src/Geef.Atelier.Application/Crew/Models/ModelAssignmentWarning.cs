namespace Geef.Atelier.Application.Crew.Models;

/// <summary>A model assignment that did not go as planned: a fallback model or a binding that breaks model plurality.</summary>
/// <param name="ActorName">Profile name of the affected actor.</param>
/// <param name="Detail">Technical reason produced by <see cref="ModelChooser"/> or the reviewer assignment.</param>
public sealed record ModelAssignmentWarning(string ActorName, string Detail);
