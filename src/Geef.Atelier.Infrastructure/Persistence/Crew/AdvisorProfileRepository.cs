using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class AdvisorProfileRepository(AtelierDbContext db) : IAdvisorProfileRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var custom = await db.AdvisorProfiles.AsNoTracking().ToListAsync(cancellationToken);
        if (!includeSystem)
            return custom;
        return SystemCrew.AdvisorProfiles.Values.Concat(custom).ToList();
    }

    /// <inheritdoc/>
    public async Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.AdvisorProfiles.TryGetValue(name, out var systemProfile))
            return systemProfile;
        return await db.AdvisorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(AdvisorProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(profile.Name))
            throw new InvalidOperationException($"Advisor profile '{profile.Name}' is a system profile and cannot be persisted to the database.");
        db.AdvisorProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(profile).State = EntityState.Detached;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(AdvisorProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(profile.Name))
            throw new InvalidOperationException($"Advisor profile '{profile.Name}' is a system profile and cannot be modified.");
        var existing = await db.AdvisorProfiles.FirstOrDefaultAsync(a => a.Name == profile.Name, cancellationToken)
            ?? throw new InvalidOperationException($"Advisor profile '{profile.Name}' not found in the database.");
        db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(name))
            throw new InvalidOperationException($"Advisor profile '{name}' is a system profile and cannot be deleted.");
        var affected = await db.AdvisorProfiles
            .Where(a => a.Name == name)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Advisor profile '{name}' not found in the database.");
    }
}
