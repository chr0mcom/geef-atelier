using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class GroundingActorCostRepository(AtelierDbContext db) : IGroundingActorCostRepository
{
    /// <inheritdoc/>
    public async Task AddAsync(GroundingActorCost cost, CancellationToken ct)
    {
        db.GroundingActorCosts.Add(cost);
        await db.SaveChangesAsync(ct);
    }
}
