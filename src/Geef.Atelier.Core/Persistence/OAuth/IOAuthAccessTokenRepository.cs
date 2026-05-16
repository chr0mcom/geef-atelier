using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthAccessTokenRepository
{
    Task AddAsync(OAuthAccessToken token, CancellationToken ct);
    Task<OAuthAccessToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeByUserIdAsync(string userId, CancellationToken ct);
    Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct);
    Task DeleteExpiredAsync(CancellationToken ct);
    /// <summary>Returns all non-revoked, non-expired access tokens for the given user.</summary>
    Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensByUserIdAsync(string userId, CancellationToken ct);
}
