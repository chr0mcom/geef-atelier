using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class GroundingProviderProfileRepository(AtelierDbContext db) : IGroundingProviderProfileRepository
{
    public async Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(CancellationToken ct)
        => await db.GroundingProviderProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public Task<GroundingProviderProfile?> GetByNameAsync(string name, CancellationToken ct)
        => db.GroundingProviderProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, ct);

    public async Task<GroundingProviderProfile> CreateAsync(GroundingProviderProfile profile, CancellationToken ct)
    {
        db.GroundingProviderProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        db.Entry(profile).State = EntityState.Detached;
        return profile;
    }

    public async Task<GroundingProviderProfile> UpdateAsync(GroundingProviderProfile profile, CancellationToken ct)
    {
        var existing = await db.GroundingProviderProfiles.FirstOrDefaultAsync(p => p.Name == profile.Name, ct)
            ?? throw new InvalidOperationException($"Grounding-provider profile '{profile.Name}' not found.");
        db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var affected = await db.GroundingProviderProfiles
            .Where(p => p.Name == oldName)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, newName), ct);
        if (affected == 0)
            throw new InvalidOperationException($"Grounding-provider profile '{oldName}' not found.");
        await CrewTemplateCascade.RenameListRefAsync(
            db, CrewTemplateCascade.ListRef.Grounding, oldName, newName, ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        var existing = await db.GroundingProviderProfiles.FirstOrDefaultAsync(p => p.Name == name, ct)
            ?? throw new InvalidOperationException($"Grounding-provider profile '{name}' not found.");
        db.GroundingProviderProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}
