using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Model-assignment double that leaves every binding unchanged, for tests unrelated to the model matrix.</summary>
internal sealed class PassThroughModelAssignmentService : IModelAssignmentService
{
    public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) =>
        Task.FromResult(new ModelAssignment(ModelMatrix.Primary(slot, domain), IsFallback: false, Warning: null));

    public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(
        IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModelAssignment>>(reviewers.Select(r => new ModelAssignment(r.Current, false, null)).ToList());

    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, CancellationToken ct = default) => Task.FromResult(snapshot);

    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) =>
        Task.FromResult(snapshot);
}
