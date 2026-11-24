using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Application.Crew.Models;

/// <summary>
/// Assigns concrete models to managed actors from <see cref="ModelMatrix"/> (D-066). This is the only place
/// where the live model catalog, the executor exclusion, cross-family reviewer ordering, the reviewer ring
/// and the paid-reviewer cap are applied. It runs once per run, at snapshot-build time.
/// </summary>
public interface IModelAssignmentService
{
    /// <summary>
    /// Assigns a single actor. With <paramref name="executor"/> the executor's model is excluded (advisors);
    /// reviewer slots additionally prefer a vendor family different from the executor's.
    /// </summary>
    /// <param name="slot">Role of the actor.</param>
    /// <param name="domain">Effective domain (profile override or crew domain).</param>
    /// <param name="executor">Effective executor binding, or <c>null</c> when assigning the executor itself.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default);

    /// <summary>
    /// Assigns all reviewers of one crew together, in crew order: manual reviewers keep their binding, managed
    /// reviewers of the same slot rotate through a ring of the first eligible candidates, and at most
    /// <see cref="ModelMatrix.MaxPaidReviewersPerCrew"/> reviewers end up on pay-per-token models.
    /// </summary>
    /// <param name="reviewers">Reviewers in crew order.</param>
    /// <param name="crewDomain">Domain of the crew.</param>
    /// <param name="executor">Effective executor binding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One assignment per input, index-aligned.</returns>
    Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(
        IReadOnlyList<ReviewerAssignmentRequest> reviewers,
        ModelDomain crewDomain,
        ModelCandidate executor,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <paramref name="snapshot"/> with every managed actor (profiles with a model slot, grounding
    /// refiners and Transform finalizers carrying a slot marker) bound to its assigned model. Manual actors
    /// are returned unchanged. Never throws because of catalog problems.
    /// </summary>
    Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, CancellationToken ct = default) =>
        ApplyAsync(snapshot, new List<ModelAssignmentWarning>(), ct);

    /// <summary>
    /// Like <see cref="ApplyAsync(CrewSnapshot, CancellationToken)"/>, and adds one entry to
    /// <paramref name="warnings"/> for every assignment that fell back or breaks model plurality.
    /// </summary>
    Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default);
}
