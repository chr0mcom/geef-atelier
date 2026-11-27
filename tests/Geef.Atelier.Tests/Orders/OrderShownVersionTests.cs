using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="OrderStateResolver.SelectShownRun"/> and <see cref="OrderOptionsExtensions.ReviseModeFor"/>.</summary>
public sealed class OrderShownVersionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Aborted)]
    public void While_or_after_a_newer_version_without_text_the_version_before_stays_shown(RunStatus newer)
    {
        var order = Guid.NewGuid();
        var v1 = Run("u", order, 1, status: RunStatus.Completed, at: T0, finalText: "Version one.");
        var v2 = Run("u", order, 2, status: newer, at: T0.AddMinutes(5));

        Assert.Same(v1, OrderStateResolver.SelectShownRun([v2, v1]));
        Assert.Same(v2, OrderStateResolver.SelectCurrentRun([v2, v1]));
    }

    [Fact]
    public void The_newest_version_with_text_is_shown_and_blank_texts_never()
    {
        var order = Guid.NewGuid();
        var v1 = Run("u", order, 1, status: RunStatus.Completed, at: T0, finalText: "One.");
        var v2 = Run("u", order, 2, status: RunStatus.Failed, at: T0.AddMinutes(1), finalText: "Two, with open points.");
        var v3 = Run("u", order, 3, status: RunStatus.Failed, at: T0.AddMinutes(2), finalText: "   ");

        Assert.Same(v2, OrderStateResolver.SelectShownRun([v1, v2, v3]));
        Assert.Null(OrderStateResolver.SelectShownRun([v3]));
        Assert.Null(OrderStateResolver.SelectShownRun([]));
    }

    [Fact]
    public void Changing_a_text_follows_the_override_of_the_owner_before_the_global_mode()
    {
        var options = new OrderOptions { Revise = ReviseMode.Off };
        options.ReviseOverrides["Tester"] = ReviseMode.On;

        Assert.Equal(ReviseMode.On, options.ReviseModeFor("tester"));
        Assert.Equal(ReviseMode.Off, options.ReviseModeFor("someone-else"));
        Assert.Equal(ReviseMode.Off, options.ReviseModeFor(null));
        Assert.Equal(ReviseMode.Off, new OrderOptions().ReviseModeFor("tester"));
    }
}
