using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for <see cref="GroundingConsultation"/> records.
/// </summary>
public interface IGroundingConsultationRepository
{
    /// <summary>Persists a new grounding consultation record and returns it.</summary>
    Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct);

    /// <summary>Returns all grounding consultations that belong to the specified run, ordered by creation time.</summary>
    Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct);

    /// <summary>
    /// Updates the <see cref="GroundingConsultation.RefinementOutcome"/> of the consultation
    /// identified by <paramref name="consultationId"/>. No-op if the consultation does not exist.
    /// </summary>
    Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct);
}
