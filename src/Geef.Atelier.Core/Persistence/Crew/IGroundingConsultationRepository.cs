using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for <see cref="GroundingConsultation"/> records.
/// Consultations are append-only within a run; there is no update or delete operation.
/// </summary>
public interface IGroundingConsultationRepository
{
    /// <summary>Persists a new grounding consultation record and returns it.</summary>
    Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct);

    /// <summary>Returns all grounding consultations that belong to the specified run, ordered by creation time.</summary>
    Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct);
}
