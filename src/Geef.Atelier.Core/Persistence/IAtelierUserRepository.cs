using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

public interface IAtelierUserRepository
{
    Task<AtelierUser?> FindByUsernameAsync(string username, CancellationToken ct);
    Task<AtelierUser?> FindByUserIdAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<AtelierUser>> GetAllAsync(CancellationToken ct);
    Task AddAsync(AtelierUser user, CancellationToken ct);
    Task UpdateAsync(AtelierUser user, CancellationToken ct);
    Task DeleteAsync(string userId, CancellationToken ct);
}
