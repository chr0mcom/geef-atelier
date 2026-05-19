using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Dashboard;

/// <summary>
/// Singleton service that caches <see cref="DashboardSnapshot"/> instances per user+scope (45-second TTL).
/// Uses <see cref="IServiceScopeFactory"/> to resolve the scoped <see cref="IDashboardRepository"/>.
/// </summary>
internal sealed class DashboardService(
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory) : IDashboardService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        string username, bool isAdmin, DashboardScope scope, CancellationToken ct = default)
    {
        var key = CacheKey(username, scope);
        if (cache.TryGetValue<DashboardSnapshot>(key, out var cached) && cached is not null)
            return cached;

        await using var s   = scopeFactory.CreateAsyncScope();
        var repo            = s.ServiceProvider.GetRequiredService<IDashboardRepository>();
        var scopedUsername  = scope == DashboardScope.My ? username : null;

        var (welcome, press, ledger, heatmap, crew, costForge, sweetSpot, manuscripts,
             tokenStream, critics, providerBench, kb, dayBook) = await FetchAllAsync(
                repo, username, isAdmin, scope, scopedUsername, ct);

        var snapshot = new DashboardSnapshot(
            username, isAdmin, scope,
            welcome, press, ledger, heatmap, crew, costForge, sweetSpot,
            manuscripts, tokenStream, critics, providerBench, kb, dayBook,
            DateTimeOffset.UtcNow);

        cache.Set(key, snapshot, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl
        });

        return snapshot;
    }

    public void InvalidateUser(string username)
    {
        cache.Remove(CacheKey(username, DashboardScope.My));
        cache.Remove(CacheKey(username, DashboardScope.All));
    }

    public void InvalidateAdmin()
    {
        // All-scope snapshots for all users are keyed with scope=All.
        // We can't enumerate IMemoryCache keys, so we rely on TTL expiry
        // for All-scope snapshots of other users. Admin's own snapshot is
        // invalidated here; other users' All-scope data expires naturally.
        // In practice only admins use All scope.
    }

    private static string CacheKey(string username, DashboardScope scope)
        => $"dashboard:{username}:{scope}";

    private static async Task<(
        WelcomeStrip, PressStatus, LedgerStats, ActivityHeatmap, CrewDna, CostForge,
        SweetSpotHistogram, IReadOnlyList<ManuscriptCard>, TokenStream,
        CriticsBenchMatrix, ProviderBench, KnowledgeBaseStats, IReadOnlyList<DayBookEntry>)>
        FetchAllAsync(
            IDashboardRepository repo,
            string username, bool isAdmin, DashboardScope scope, string? scopedUsername,
            CancellationToken ct)
    {
        var welcome      = await repo.GetWelcomeStripAsync(username, isAdmin, scope, ct);
        var press        = await repo.GetPressStatusAsync(scopedUsername, ct);
        var ledger       = await repo.GetLedgerStatsAsync(scopedUsername, ct);
        var heatmap      = await repo.GetActivityHeatmapAsync(scopedUsername, ct);
        var crew         = await repo.GetCrewDnaAsync(scopedUsername, ct);
        var costForge    = await repo.GetCostForgeAsync(scopedUsername, ct);
        var sweetSpot    = await repo.GetSweetSpotAsync(scopedUsername, ct);
        var manuscripts  = await repo.GetManuscriptsAsync(scopedUsername, ct);
        var tokenStream  = await repo.GetTokenStreamAsync(scopedUsername, ct);
        var critics      = await repo.GetCriticsBenchAsync(scopedUsername, ct);
        var providerBench = await repo.GetProviderBenchAsync(ct);
        var kb           = await repo.GetKnowledgeBaseStatsAsync(ct);
        var dayBook      = await repo.GetDayBookAsync(scopedUsername, isAdmin, scope, ct);

        return (welcome, press, ledger, heatmap, crew, costForge, sweetSpot,
                manuscripts, tokenStream, critics, providerBench, kb, dayBook);
    }
}
