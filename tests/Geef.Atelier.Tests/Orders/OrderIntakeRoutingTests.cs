using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Orders;

/// <summary>The truth table of the <c>SubmitAsync</c> switch: mode × questions allowed × custom crew × request longer than the limit.</summary>
public sealed class OrderIntakeRoutingTests
{
    private const int Limit = 8_000;

    public static TheoryData<IntakeMode, bool, bool, int, string> Table()
    {
        var data = new TheoryData<IntakeMode, bool, bool, int, string>();
        foreach (var mode in Enum.GetValues<IntakeMode>())
        foreach (var allowQuestions in new[] { true, false })
        foreach (var customCrew in new[] { true, false })
        foreach (var chars in new[] { Limit, Limit + 1 })
        {
            var eligible = allowQuestions && !customCrew && chars <= Limit;
            var expected = (mode, eligible) switch
            {
                (IntakeMode.On, true) => OrderIntakeRoute.Clarify,
                (IntakeMode.Shadow, true) => OrderIntakeRoute.StartNowWithShadow,
                _ => OrderIntakeRoute.StartNow,
            };
            data.Add(mode, allowQuestions, customCrew, chars, expected.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Every_combination_takes_the_documented_route(IntakeMode mode, bool allowQuestions, bool customCrew, int chars, string expected) =>
        Assert.Equal(expected, OrderIntakeRouting.Decide(mode, allowQuestions, customCrew, chars, Limit).ToString());

    [Fact]
    public void The_table_has_all_24_rows_and_exactly_one_clarifies_and_one_shadows()
    {
        var rows = Table().Select(r => Enum.Parse<OrderIntakeRoute>((string)r[4])).ToList();
        Assert.Equal(24, rows.Count);
        Assert.Equal(1, rows.Count(r => r == OrderIntakeRoute.Clarify));
        Assert.Equal(1, rows.Count(r => r == OrderIntakeRoute.StartNowWithShadow));
    }

    [Fact]
    public void Very_short_requests_are_clarified_too() =>
        Assert.Equal(OrderIntakeRoute.Clarify, OrderIntakeRouting.Decide(IntakeMode.On, true, false, 1, Limit));
}
