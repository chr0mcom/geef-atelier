using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class AtelierUserRepository(AtelierDbContext db) : IAtelierUserRepository
{
    public async Task<AtelierUser?> FindByUsernameAsync(string username, CancellationToken ct)
        => await db.Set<AtelierUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task<AtelierUser?> FindByUserIdAsync(string userId, CancellationToken ct)
        => await db.Set<AtelierUser>().AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<IReadOnlyList<AtelierUser>> GetAllAsync(CancellationToken ct)
        => await db.Set<AtelierUser>().AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);

    public async Task AddAsync(AtelierUser user, CancellationToken ct)
    {
        db.Set<AtelierUser>().Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AtelierUser user, CancellationToken ct)
    {
        await db.Set<AtelierUser>()
            .Where(u => u.UserId == user.UserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Username, user.Username)
                .SetProperty(u => u.PasswordHash, user.PasswordHash)
                .SetProperty(u => u.Email, user.Email)
                .SetProperty(u => u.IsActive, user.IsActive)
                .SetProperty(u => u.IsAdmin, user.IsAdmin)
                .SetProperty(u => u.UpdatedAt, user.UpdatedAt), ct);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct)
    {
        await db.Set<AtelierUser>()
            .Where(u => u.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
