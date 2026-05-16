using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthAccessTokenRepository : IOAuthAccessTokenRepository
{
    private readonly Dictionary<string, OAuthAccessToken> _store = new();

    public Task AddAsync(OAuthAccessToken token, CancellationToken ct)
    {
        _store[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<OAuthAccessToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(tokenHash));

    public Task RevokeByUserIdAsync(string userId, CancellationToken ct)
    {
        var keys = _store.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        foreach (var k in keys)
            _store[k] = _store[k] with { RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct)
    {
        var keys = _store
            .Where(kv => kv.Value.ClientId == clientId && kv.Value.UserId == userId)
            .Select(kv => kv.Key).ToList();
        foreach (var k in keys)
            _store[k] = _store[k] with { RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensByUserIdAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = _store.Values
            .Where(t => t.UserId == userId && t.RevokedAt is null && t.ExpiresAt > now)
            .ToList();
        return Task.FromResult<IReadOnlyList<OAuthAccessToken>>(active);
    }

    public Task DeleteExpiredAsync(CancellationToken ct)
    {
        var expired = _store.Where(kv => kv.Value.ExpiresAt < DateTimeOffset.UtcNow)
            .Select(kv => kv.Key).ToList();
        foreach (var k in expired) _store.Remove(k);
        return Task.CompletedTask;
    }
}
