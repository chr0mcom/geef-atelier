using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Tests.Domain;

public sealed class WelcomeStatsTests
{
    [Fact]
    public void WelcomeStats_PreservesFields()
    {
        var stats = new WelcomeStats(
            RunsThisMonth: 42,
            ConvergenceRate: 0.75,
            AverageIterations: 3.1,
            TotalCostThisMonth: 2.80m);

        Assert.Equal(42, stats.RunsThisMonth);
        Assert.Equal(0.75, stats.ConvergenceRate);
        Assert.Equal(3.1, stats.AverageIterations);
        Assert.Equal(2.80m, stats.TotalCostThisMonth);
    }
}
