using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class FinalizerProfileRepository(AtelierDbContext db) : IFinalizerProfileRepository
{
    public async Task<IReadOnlyList<FinalizerProfile>> ListAsync(CancellationToken ct)
        => await db.FinalizerProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public Task<FinalizerProfile?> GetByNameAsync(string name, CancellationToken ct)
        => db.FinalizerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, ct);

    public async Task<FinalizerProfile> CreateAsync(FinalizerProfile profile, CancellationToken ct)
    {
        db.FinalizerProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        db.Entry(profile).State = EntityState.Detached;
        return profile;
    }

    public async Task<FinalizerProfile> UpdateAsync(FinalizerProfile profile, CancellationToken ct)
    {
        var existing = await db.FinalizerProfiles.FirstOrDefaultAsync(p => p.Name == profile.Name, ct)
            ?? throw new InvalidOperationException($"Finalizer profile '{profile.Name}' not found.");
        db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var affected = await db.FinalizerProfiles
            .Where(p => p.Name == oldName)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, newName), ct);
        if (affected == 0)
            throw new InvalidOperationException($"Finalizer profile '{oldName}' not found.");
        await CrewTemplateCascade.RenameListRefAsync(
            db, CrewTemplateCascade.ListRef.Finalizer, oldName, newName, ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        var existing = await db.FinalizerProfiles.FirstOrDefaultAsync(p => p.Name == name, ct)
            ?? throw new InvalidOperationException($"Finalizer profile '{name}' not found.");
        db.FinalizerProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}
