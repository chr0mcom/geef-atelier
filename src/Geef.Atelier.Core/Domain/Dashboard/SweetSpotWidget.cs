namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Iteration-count histogram showing where convergence typically occurs.</summary>
public sealed record SweetSpotHistogram(
    IReadOnlyList<SweetSpotBucket> Buckets,
    double MeanIterations,
    int TotalRuns);

public sealed record SweetSpotBucket(int Iterations, int Count, double Share);
