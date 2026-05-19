using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Core.Persistence;

/// <summary>
/// Data-access contract for the dashboard.
/// Pass <c>username = null</c> for all-scope queries (established null-bypass pattern).
/// </summary>
public interface IDashboardRepository
{
    Task<WelcomeStrip> GetWelcomeStripAsync(string username, bool isAdmin, DashboardScope scope, CancellationToken ct);
    Task<PressStatus> GetPressStatusAsync(string? username, CancellationToken ct);
    Task<LedgerStats> GetLedgerStatsAsync(string? username, CancellationToken ct);
    Task<ActivityHeatmap> GetActivityHeatmapAsync(string? username, CancellationToken ct);
    Task<CrewDna> GetCrewDnaAsync(string? username, CancellationToken ct);
    Task<CostForge> GetCostForgeAsync(string? username, CancellationToken ct);
    Task<SweetSpotHistogram> GetSweetSpotAsync(string? username, CancellationToken ct);
    Task<IReadOnlyList<ManuscriptCard>> GetManuscriptsAsync(string? username, CancellationToken ct);
    Task<TokenStream> GetTokenStreamAsync(string? username, CancellationToken ct);
    Task<CriticsBenchMatrix> GetCriticsBenchAsync(string? username, CancellationToken ct);
    Task<ProviderBench> GetProviderBenchAsync(CancellationToken ct);
    Task<KnowledgeBaseStats> GetKnowledgeBaseStatsAsync(CancellationToken ct);
    Task<IReadOnlyList<DayBookEntry>> GetDayBookAsync(string? username, bool isAdmin, DashboardScope scope, CancellationToken ct);
}
