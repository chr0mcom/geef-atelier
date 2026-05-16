using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthClientRepository
{
    Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct);
    Task AddAsync(OAuthClient client, CancellationToken ct);
}
