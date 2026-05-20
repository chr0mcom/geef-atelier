using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for <see cref="GroundingActorCost"/> records.
/// Costs are append-only; there is no update or delete operation.
/// </summary>
public interface IGroundingActorCostRepository
{
    /// <summary>Persists a new grounding actor cost record.</summary>
    Task AddAsync(GroundingActorCost cost, CancellationToken ct);
}
