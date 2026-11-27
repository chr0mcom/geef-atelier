namespace Geef.Atelier.Core.Domain;

/// <summary>Aggregated statistics shown on the Welcome page.</summary>
public sealed record WelcomeStats(
    int RunsThisMonth,
    double ConvergenceRate,
    double AverageIterations,
    decimal TotalCostThisMonth);
