using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthClientRepository : IOAuthClientRepository
{
    private readonly Dictionary<string, OAuthClient> _store = new();

    public Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(clientId));

    public Task AddAsync(OAuthClient client, CancellationToken ct)
    {
        _store[client.ClientId] = client;
        return Task.CompletedTask;
    }
}
