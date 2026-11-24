using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

/// <summary>A resolved crew snapshot together with the model-assignment warnings produced while building it.</summary>
/// <param name="Snapshot">The fully dereferenced snapshot.</param>
/// <param name="Warnings">Assignment warnings, to be stored on the run once it exists.</param>
public sealed record ResolvedCrewSnapshot(CrewSnapshot Snapshot, IReadOnlyList<ModelAssignmentWarning> Warnings);
