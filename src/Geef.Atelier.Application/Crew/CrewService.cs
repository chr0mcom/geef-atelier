using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

internal sealed class CrewService(IModelCatalog modelCatalog, IModelAssignmentService modelAssignment) : ICrewService
{
    public async Task<ResolvedCrewSnapshot> ResolveCodeTemplateSnapshotAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var warnings = new List<ModelAssignmentWarning>();
        var snapshot = CrewSnapshotBuilder.Build(template, SystemCrew.CodeOnlyExecutorProfiles, SystemCrew.CodeOnlyReviewerProfiles);
        var assigned = await modelAssignment.ApplyAsync(snapshot, warnings, cancellationToken);
        return new(await CrewSnapshotModelResolver.ResolveAsync(assigned, modelCatalog, cancellationToken), warnings);
    }
}
