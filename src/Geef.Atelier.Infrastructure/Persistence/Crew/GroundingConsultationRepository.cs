using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class GroundingConsultationRepository(AtelierDbContext db) : IGroundingConsultationRepository
{
    /// <inheritdoc/>
    public async Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
    {
        db.GroundingConsultations.Add(consultation);
        await db.SaveChangesAsync(ct);
        return consultation;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
        => await db.GroundingConsultations
            .AsNoTracking()
            .Where(c => c.RunId == runId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
}
