namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>365-day activity heatmap (GitHub-style calendar).</summary>
public sealed record ActivityHeatmap(
    IReadOnlyList<HeatmapCell> Cells,
    PeakAttribution Peak);

/// <summary>One day in the heatmap grid.</summary>
public sealed record HeatmapCell(DateOnly Date, int Count, int Level);  // Level 0-4

/// <summary>The single busiest day in the heatmap window.</summary>
public sealed record PeakAttribution(DateOnly Date, int Count);
