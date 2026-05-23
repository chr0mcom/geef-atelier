using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class ProviderCatalog(IServiceScopeFactory scopeFactory) : IProviderCatalog
{
    private volatile CachedResult? _cached;

    private sealed record CachedResult(IReadOnlyList<ProviderInfo> Items, DateTimeOffset Expiry);

    public IReadOnlyList<ProviderInfo> ListProviders()
    {
        var cached = _cached;
        if (cached is not null && DateTimeOffset.UtcNow < cached.Expiry)
            return cached.Items;

        // Offload to the thread pool: callers may run on a single-threaded SynchronizationContext
        // (e.g. a Blazor Server circuit). Calling the async API with .GetResult() directly there
        // deadlocks, because the awaited continuation needs the very thread that is blocked here.
        // Task.Run executes without the captured context, so the continuation resumes on a pool thread.
        var providers = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IProviderService>();
            return await service.ListAsync(includeInactive: false);
        }).GetAwaiter().GetResult();
        var items = providers
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProviderInfo(p.Name, p.DisplayName))
            .ToList();
        _cached = new CachedResult(items, DateTimeOffset.UtcNow.AddMinutes(5));
        return items;
    }
}
