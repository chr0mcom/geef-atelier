using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

/// <summary>Builds the snapshots of the setups that exist only in code (<see cref="SystemCrew.CodeOnlyTemplates"/>).</summary>
public interface ICrewService
{
    /// <summary>
    /// Builds the snapshot of <paramref name="template"/> from the code-only profiles of <see cref="SystemCrew"/>, assigns the models of
    /// the model matrix and resolves model aliases to the concrete models they point at now. The warnings name every model that could
    /// not be assigned as planned.
    /// </summary>
    Task<ResolvedCrewSnapshot> ResolveCodeTemplateSnapshotAsync(CrewTemplate template, CancellationToken cancellationToken = default);
}
