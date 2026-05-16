using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthClientRepository
{
    Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct);
    Task<IReadOnlyList<OAuthClient>> GetAllAsync(CancellationToken ct);
    Task AddAsync(OAuthClient client, CancellationToken ct);
    Task DeleteAsync(string clientId, CancellationToken ct);
}
