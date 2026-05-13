using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for <see cref="AdvisorConsultation"/> records. Consultations are append-only
/// within a run; there is no update or delete operation.
/// </summary>
public interface IAdvisorConsultationRepository
{
    /// <summary>Persists a new advisor consultation record and returns it (with any DB-generated values applied).</summary>
    Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct);

    /// <summary>Returns all advisor consultations that belong to the specified run, ordered by creation time.</summary>
    Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct);
}
