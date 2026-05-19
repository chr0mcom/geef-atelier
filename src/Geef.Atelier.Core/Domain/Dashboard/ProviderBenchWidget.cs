namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Per-provider request count, cost and availability summary (30-day).</summary>
public sealed record ProviderBench(IReadOnlyList<ProviderRow> Rows);

public sealed record ProviderRow(
    string ProviderName,
    string DisplayName,
    ProviderState State,
    int RequestCount,
    decimal CostEur,
    long InputTokens,
    long OutputTokens);

public enum ProviderState
{
    Active  = 0,
    Idle    = 1,
    Unknown = 2
}
