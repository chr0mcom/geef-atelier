using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class AdvisorConsultationRepository(AtelierDbContext db) : IAdvisorConsultationRepository
{
    /// <inheritdoc/>
    public async Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct)
    {
        db.AdvisorConsultations.Add(consultation);
        await db.SaveChangesAsync(ct);
        return consultation;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
    {
        return await db.AdvisorConsultations
            .AsNoTracking()
            .Where(c => c.RunId == runId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
    }
}
