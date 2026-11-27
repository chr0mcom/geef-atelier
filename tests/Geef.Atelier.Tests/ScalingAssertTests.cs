using Xunit.Sdk;

namespace Geef.Atelier.Tests;

/// <summary><see cref="ScalingAssert"/> itself, with waiting in place of work so that the growth is known.</summary>
public sealed class ScalingAssertTests
{
    private const int SmallMilliseconds = 40;

    [Fact]
    public void Linear_growth_passes() =>
        ScalingAssert.GrowsAboutLinearly(percent => () => Thread.Sleep(SmallMilliseconds * percent / 25));

    [Fact]
    public void Quadratic_growth_fails_also_after_all_further_rounds()
    {
        var largeRuns = 0;

        var failure = Assert.Throws<TrueException>(() => ScalingAssert.GrowsAboutLinearly(percent => () =>
        {
            largeRuns += percent == 100 ? 1 : 0;
            Thread.Sleep(SmallMilliseconds * percent / 25 * percent / 25);
        }));

        Assert.Contains("times as long", failure.Message, StringComparison.Ordinal);
        Assert.Equal(12, largeRuns);
    }

    [Fact]
    public void A_busy_phase_that_slows_the_first_large_runs_does_not_fail_linear_work()
    {
        var largeRuns = 0;

        ScalingAssert.GrowsAboutLinearly(percent => () =>
        {
            var slowed = percent == 100 && ++largeRuns <= 4;
            Thread.Sleep(SmallMilliseconds * percent / 25 * (slowed ? 5 : 1));
        });

        Assert.Equal(5, largeRuns);
    }
}
