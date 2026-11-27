using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Base implementation of <see cref="ICrewService"/> that returns the template's code-only profiles without model assignment.
/// Subclass and override only what your test needs.
/// </summary>
internal abstract class StubCrewServiceBase : ICrewService
{
    public virtual Task<ResolvedCrewSnapshot> ResolveCodeTemplateSnapshotAsync(CrewTemplate template, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolvedCrewSnapshot(
            new CrewSnapshot(CrewSnapshot.CurrentSchemaVersion, template.Name, SystemCrew.DirectWriterProfile, [],
                template.EvaluationStrategy, template.ConvergenceOverride),
            []));
}
