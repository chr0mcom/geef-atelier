using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class ReviewerProfileRepository(AtelierDbContext db) : IReviewerProfileRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var custom = await db.ReviewerProfiles.AsNoTracking().ToListAsync(cancellationToken);
        if (!includeSystem)
            return custom;
        return SystemCrew.ReviewerProfiles.Values.Concat(custom).ToList();
    }

    /// <inheritdoc/>
    public async Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.ReviewerProfiles.TryGetValue(name, out var systemProfile))
            return systemProfile;
        return await db.ReviewerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(ReviewerProfile profile, CancellationToken cancellationToken = default)
    {
        db.ReviewerProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(profile).State = EntityState.Detached;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ReviewerProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await db.ReviewerProfiles.FirstOrDefaultAsync(r => r.Name == profile.Name, cancellationToken)
            ?? throw new InvalidOperationException($"Reviewer profile '{profile.Name}' not found in the database.");
        db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.ReviewerProfiles
            .Where(r => r.Name == oldName)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, newName), cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Reviewer profile '{oldName}' not found in the database.");
        await CrewTemplateCascade.RenameListRefAsync(
            db, CrewTemplateCascade.ListRef.Reviewer, oldName, newName, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var affected = await db.ReviewerProfiles
            .Where(r => r.Name == name)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Reviewer profile '{name}' not found in the database.");
    }
}
