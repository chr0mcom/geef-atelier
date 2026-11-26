using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// "What was checked" under the text: always there when the result carries the sentences, even without any finding; it shows
/// the sentences of <see cref="OrderCheckLines"/> unchanged, says first what the person has to look at, and ends with the
/// fixed limits. It never adds a line of its own.
/// </summary>
public sealed class OrderChecksPanelTests : TestContext
{
    private static OrderResult Result(OrderChecksView? checks) =>
        OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with { Checks = checks };

    [Fact]
    public void WithoutAnyFindingTheBlockIsShownWithItsChecksAndTheFixedEnd()
    {
        var checks = new OrderChecksView(
            [OrderCheckLines.AutomaticLead + OrderCheckLines.PassedNotEmpty + ", " + OrderCheckLines.PassedStartsDirectly + OrderCheckLines.AutomaticEnd, OrderCheckLines.FactsChecked],
            [], OrderCheckLines.ToReviewNothing, OrderCheckLines.Limits);

        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(checks)));

        Assert.Equal("What was checked", cut.Find("[data-testid='order-checks'] h2").TextContent);
        Assert.Equal(new[] { "Checked automatically: the text is not empty, starts with the text itself, not with a remark to the reader.", OrderCheckLines.FactsChecked },
            cut.FindAll("[data-testid='order-checks-line']").Select(e => e.TextContent));
        Assert.Equal("There is nothing from these checks that you need to look at.", cut.Find("[data-testid='order-checks-review']").TextContent);
        Assert.Equal("false", cut.Find("[data-testid='order-checks-review']").GetAttribute("data-open"));
        Assert.Equal("These checks look for the problems named here. They do not prove that everything in the text is right.",
            cut.Find("[data-testid='order-checks-limits']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-checks-changed']"));
        Assert.DoesNotContain("Changed after the check", cut.Markup);
    }

    [Fact]
    public void FactsNotCheckedIsSaidPlainly()
    {
        var checks = new OrderChecksView([OrderCheckLines.AutomaticLead + OrderCheckLines.PassedNotEmpty + OrderCheckLines.AutomaticEnd, OrderCheckLines.FactsNotChecked],
            [], OrderCheckLines.ToReviewFactsNotFullyChecked, OrderCheckLines.Limits);

        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(checks)));

        Assert.Contains("Facts were not checked.", cut.FindAll("[data-testid='order-checks-line']").Select(e => e.TextContent));
        Assert.DoesNotContain("compared with your request", cut.Markup);
    }

    [Fact]
    public void OnlyTheGivenLinesAreShownAndNoCheckIsAdded()
    {
        var checks = new OrderChecksView([OrderCheckLines.FactsNotCompleted], [], OrderCheckLines.ToReviewFactsNotFullyChecked, OrderCheckLines.Limits);

        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(checks)));

        Assert.Equal(OrderCheckLines.FactsNotCompleted, Assert.Single(cut.FindAll("[data-testid='order-checks-line']")).TextContent);
        Assert.DoesNotContain("Totals", cut.Markup);
        Assert.DoesNotContain("contradictions", cut.Markup);
        Assert.DoesNotContain("Checked automatically", cut.Markup);
        var text = cut.Find("[data-testid='order-checks']").TextContent;
        var known = new[] { OrderCheckLines.Heading, OrderCheckLines.FactsNotCompleted, OrderCheckLines.ToReviewFactsNotFullyChecked, OrderCheckLines.Limits };
        Assert.Equal(Squeeze(string.Concat(known)), Squeeze(text));
    }

    private static string Squeeze(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public void ChangesAfterTheCheckAppearOnlyAfterACorrection()
    {
        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(HonestResultData.Checks(openPoints: true, changed: true))));

        Assert.Contains("Changed after the check:", cut.Markup);
        Assert.Equal(new[] { "Removed or replaced: “Bitte geben Sie uns in der Gruppe Bescheid.”", "Shortened to the requested length." },
            cut.FindAll("[data-testid='order-checks-change']").Select(e => e.TextContent));
    }

    [Fact]
    public void WhatToLookAtComesAfterTheChecksAndChangesAndIsMarkedWhenThereAreOpenPoints()
    {
        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(HonestResultData.Checks(openPoints: true, changed: true))));

        var block = cut.Find("[data-testid='order-checks']");
        Assert.Contains("order-checks--review", block.ClassName);
        var review = cut.Find("[data-testid='order-checks-review']");
        Assert.Equal("Please look at the open points below.", review.TextContent);
        Assert.Equal("true", review.GetAttribute("data-open"));
        var markup = block.OuterHtml;
        Assert.True(markup.IndexOf("order-checks-line", StringComparison.Ordinal) < markup.IndexOf("order-checks-review", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("order-checks-change\"", StringComparison.Ordinal) < markup.IndexOf("order-checks-review", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("order-checks-review", StringComparison.Ordinal) < markup.IndexOf("order-checks-limits", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutSentencesNothingIsShownSoNothingIsPromised()
    {
        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(null)));

        Assert.Empty(cut.FindAll("[data-testid='order-checks']"));
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void TheBlockUsesNoInsiderWords()
    {
        var cut = RenderComponent<OrderChecksPanel>(p => p.Add(c => c.Result, Result(HonestResultData.Checks(openPoints: true, changed: true))));

        HonestResultData.AssertPlain(cut.Nodes);
    }
}
