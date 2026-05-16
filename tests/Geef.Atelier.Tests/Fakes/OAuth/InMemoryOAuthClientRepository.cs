using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthClientRepository : IOAuthClientRepository
{
    private readonly Dictionary<string, OAuthClient> _store = new();

    public Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(clientId));

    public Task<IReadOnlyList<OAuthClient>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OAuthClient>>(_store.Values.OrderBy(c => c.ClientName).ToList());

    public Task AddAsync(OAuthClient client, CancellationToken ct)
    {
        _store[client.ClientId] = client;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string clientId, CancellationToken ct)
    {
        _store.Remove(clientId);
        return Task.CompletedTask;
    }
}
