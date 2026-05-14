using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Tests.Domain;

public sealed class WelcomeStatsTests
{
    [Fact]
    public void WelcomeStats_IncludesStudioFields()
    {
        var stats = new WelcomeStats(
            RunsThisMonth: 10,
            ConvergenceRate: 0.8,
            AverageIterations: 2.3,
            TotalCostThisMonth: 1.50m,
            StudioAnalysesThisMonth: 5,
            StudioCostThisMonth: 0.15m);

        Assert.Equal(5, stats.StudioAnalysesThisMonth);
        Assert.Equal(0.15m, stats.StudioCostThisMonth);
    }

    [Fact]
    public void WelcomeStats_DefaultStudioFieldsAreZero()
    {
        var stats = new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m);

        Assert.Equal(0, stats.StudioAnalysesThisMonth);
        Assert.Equal(0m, stats.StudioCostThisMonth);
    }

    [Fact]
    public void WelcomeStats_PreservesExistingFields()
    {
        var stats = new WelcomeStats(
            RunsThisMonth: 42,
            ConvergenceRate: 0.75,
            AverageIterations: 3.1,
            TotalCostThisMonth: 2.80m,
            StudioAnalysesThisMonth: 7,
            StudioCostThisMonth: 0.30m);

        Assert.Equal(42, stats.RunsThisMonth);
        Assert.Equal(0.75, stats.ConvergenceRate);
        Assert.Equal(3.1, stats.AverageIterations);
        Assert.Equal(2.80m, stats.TotalCostThisMonth);
    }

    [Fact]
    public void WelcomeStats_StudioCostThisMonth_AcceptsDecimalPrecision()
    {
        var stats = new WelcomeStats(0, 0.0, 0.0, 0m, 3, 0.0042m);

        Assert.Equal(0.0042m, stats.StudioCostThisMonth);
    }
}
