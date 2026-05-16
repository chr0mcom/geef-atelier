using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class ExecutorProfileRepository(AtelierDbContext db) : IExecutorProfileRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var custom = await db.ExecutorProfiles.AsNoTracking().ToListAsync(cancellationToken);
        if (!includeSystem)
            return custom;
        return SystemCrew.ExecutorProfiles.Values.Concat(custom).ToList();
    }

    /// <inheritdoc/>
    public async Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.ExecutorProfiles.TryGetValue(name, out var systemProfile))
            return systemProfile;
        return await db.ExecutorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(ExecutorProfile profile, CancellationToken cancellationToken = default)
    {
        db.ExecutorProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(profile).State = EntityState.Detached;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ExecutorProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await db.ExecutorProfiles.FirstOrDefaultAsync(e => e.Name == profile.Name, cancellationToken)
            ?? throw new InvalidOperationException($"Executor profile '{profile.Name}' not found in the database.");
        db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var affected = await db.ExecutorProfiles
            .Where(e => e.Name == name)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Executor profile '{name}' not found in the database.");
    }
}
