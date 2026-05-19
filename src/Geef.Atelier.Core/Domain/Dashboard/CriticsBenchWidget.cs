namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Per-reviewer pass/fail matrix for the last 30 days.</summary>
public sealed record CriticsBenchMatrix(IReadOnlyList<CriticsRow> Rows);

public sealed record CriticsRow(
    string ReviewerName,
    string DisplayName,
    int Passes,
    int Fails,
    double PassRate,
    string? ModelName);
