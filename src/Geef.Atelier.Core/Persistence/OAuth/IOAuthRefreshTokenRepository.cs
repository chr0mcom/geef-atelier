using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthRefreshTokenRepository
{
    Task AddAsync(OAuthRefreshToken token, CancellationToken ct);
    /// <summary>Atomically marks refresh token as used. Returns null if not found or already used/revoked.</summary>
    Task<OAuthRefreshToken?> ConsumeAsync(string tokenHash, CancellationToken ct);
    /// <summary>Finds a refresh token by hash without consuming it (used for reuse detection).</summary>
    Task<OAuthRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeByUserIdAsync(string userId, CancellationToken ct);
    Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct);
    Task DeleteExpiredAsync(CancellationToken ct);
}
