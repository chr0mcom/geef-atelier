using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthRefreshTokenRepository : IOAuthRefreshTokenRepository
{
    private readonly Dictionary<string, OAuthRefreshToken> _store = new();

    public Task AddAsync(OAuthRefreshToken token, CancellationToken ct)
    {
        _store[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<OAuthRefreshToken?> ConsumeAsync(string tokenHash, CancellationToken ct)
    {
        if (!_store.TryGetValue(tokenHash, out var token)) return Task.FromResult<OAuthRefreshToken?>(null);
        if (token.UsedAt.HasValue || token.RevokedAt.HasValue) return Task.FromResult<OAuthRefreshToken?>(null);
        _store[tokenHash] = token with { UsedAt = DateTimeOffset.UtcNow };
        return Task.FromResult<OAuthRefreshToken?>(token);
    }

    public Task<OAuthRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(tokenHash));

    public Task RevokeByHashAsync(string tokenHash, CancellationToken ct)
    {
        if (_store.TryGetValue(tokenHash, out var t) && !t.RevokedAt.HasValue)
            _store[tokenHash] = t with { RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task RevokeByUserIdAsync(string userId, CancellationToken ct)
    {
        var keys = _store.Where(kv => kv.Value.UserId == userId && !kv.Value.RevokedAt.HasValue)
            .Select(kv => kv.Key).ToList();
        foreach (var k in keys)
            _store[k] = _store[k] with { RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct)
    {
        var keys = _store
            .Where(kv => kv.Value.ClientId == clientId && kv.Value.UserId == userId && !kv.Value.RevokedAt.HasValue)
            .Select(kv => kv.Key).ToList();
        foreach (var k in keys)
            _store[k] = _store[k] with { RevokedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task DeleteExpiredAsync(CancellationToken ct)
    {
        var expired = _store.Where(kv => kv.Value.ExpiresAt < DateTimeOffset.UtcNow)
            .Select(kv => kv.Key).ToList();
        foreach (var k in expired) _store.Remove(k);
        return Task.CompletedTask;
    }
}
