using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthAuthorizationCodeRepository : IOAuthAuthorizationCodeRepository
{
    private readonly Dictionary<string, OAuthAuthorizationCode> _store = new();

    public Task AddAsync(OAuthAuthorizationCode code, CancellationToken ct)
    {
        _store[code.CodeHash] = code;
        return Task.CompletedTask;
    }

    public Task<OAuthAuthorizationCode?> FindByCodeHashAsync(string codeHash, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(codeHash));

    public Task<OAuthAuthorizationCode?> ConsumeAsync(string codeHash, CancellationToken ct)
    {
        if (!_store.TryGetValue(codeHash, out var code)) return Task.FromResult<OAuthAuthorizationCode?>(null);
        if (code.UsedAt.HasValue) return Task.FromResult<OAuthAuthorizationCode?>(null);
        var consumed = code with { UsedAt = DateTimeOffset.UtcNow };
        _store[codeHash] = consumed;
        return Task.FromResult<OAuthAuthorizationCode?>(consumed);
    }

    public Task DeleteExpiredAsync(CancellationToken ct)
    {
        var expired = _store.Where(kv => kv.Value.ExpiresAt < DateTimeOffset.UtcNow)
            .Select(kv => kv.Key).ToList();
        foreach (var k in expired) _store.Remove(k);
        return Task.CompletedTask;
    }
}
