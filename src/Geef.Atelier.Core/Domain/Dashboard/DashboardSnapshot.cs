namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>
/// Full dashboard data snapshot for one user+scope combination.
/// All 13 widget payloads are computed and cached together (45-second TTL).
/// </summary>
public sealed record DashboardSnapshot(
    string Username,
    bool IsAdmin,
    DashboardScope Scope,
    WelcomeStrip WelcomeStrip,
    PressStatus Press,
    LedgerStats Ledger,
    ActivityHeatmap Heatmap,
    CrewDna CrewDna,
    CostForge CostForge,
    SweetSpotHistogram SweetSpot,
    IReadOnlyList<ManuscriptCard> Manuscripts,
    TokenStream TokenStream,
    CriticsBenchMatrix CriticsBench,
    ProviderBench ProviderBench,
    KnowledgeBaseStats KnowledgeBase,
    IReadOnlyList<DayBookEntry> DayBook,
    DateTimeOffset GeneratedAt);
