using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The finished order page in stage 3: the text, then "What was checked", then what the person has to look at ("Open points"),
/// then "Assumptions", then "From your files". "How it was made" only points to "What was checked" and says nothing else about the checks.
/// </summary>
public sealed class OrderDetailResultTests : TestContext
{
    private readonly FakeOrderService _orders = new();

    public OrderDetailResultTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IRunUpdateFeed>(new ScriptedOrderFeed());
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("alice");
    }

    private IRenderedComponent<OrderDetail> RenderFull(Func<OrderResult, OrderResult>? change = null)
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints) with
        {
            Clarification = OrderTestData.Clarification(outcome: IntakeOutcome.Answered, language: "de"),
        });
        var result = HonestResultData.Full(view);
        _orders.SetResult(change is null ? result : change(result));
        return RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, view.OrderId));
    }

    [Fact]
    public void TheBlocksFollowTheTextInTheOrderOfWhatMattersMost()
    {
        var cut = RenderFull();

        var markup = cut.Markup;
        var positions = new[] { "order-result", "order-checks", "order-open-points", "order-assumptions", "order-looked-up", "order-sources-used", "order-how-made" }
            .Select(id => markup.IndexOf($"data-testid=\"{id}\"", StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void QuotesAndAssumptionsCarryTheLanguageOfTheOrder()
    {
        var cut = RenderFull();

        Assert.All(cut.FindAll("[data-testid='order-assumption']"), e => Assert.Equal("de", e.GetAttribute("lang")));
        Assert.All(cut.FindAll("q.order-quote"), e => Assert.Equal("de", e.GetAttribute("lang")));
        Assert.Equal("de", cut.Find("[data-testid='order-source-quote']").GetAttribute("lang"));
    }

    [Fact]
    public void HowItWasMadeOnlyPointsToWhatWasChecked()
    {
        var cut = RenderFull();

        Assert.Equal("See “What was checked” below your text.", cut.Find("[data-testid='order-how-checks']").TextContent);
        Assert.DoesNotContain("All automatic checks passed.", cut.Markup);
        Assert.DoesNotContain("checks are still open", cut.Markup);
    }

    [Fact]
    public void WhatWasCheckedIsShownEvenWithoutAnyFinding()
    {
        var cut = RenderFull(r => OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered, orderId: r.OrderId)) with
        {
            Checks = HonestResultData.Checks(openPoints: false),
            FactCheck = Geef.Atelier.Core.Domain.FactCheck.FactCheckState.Checked,
            Placeholders = [],
        });

        Assert.NotNull(cut.Find("[data-testid='order-checks']"));
        Assert.Equal("There is nothing from these checks that you need to look at.", cut.Find("[data-testid='order-checks-review']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-open-points']"));
        Assert.Empty(cut.FindAll("[data-testid='order-assumptions']"));
    }

    [Fact]
    public void TheResultPartUsesNoInsiderWords()
    {
        var cut = RenderFull();

        foreach (var id in new[] { "order-checks", "order-open-points", "order-assumptions", "order-looked-up", "order-sources-used", "order-how-checks" })
            HonestResultData.AssertPlain(cut.Find($"[data-testid='{id}']").ChildNodes);
    }

    private static RunWarningEntity Warning(string code, string message) => new()
    {
        Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Source = RunWarningSources.Grounding, Code = code, Message = message, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ResearchThatDidNotWorkIsToldOnceUnderLookedUpAndNotAgainAmongTheWarnings()
    {
        var failed = new OrderLookedUpView(ResearchStatus.Failed, OrderResearchLines.Failed, 0, []);
        var cut = RenderFull(r => r with
        {
            LookedUp = failed,
            Warnings = [Warning(RunWarningCodes.ResearchFailed, OrderResearchLines.Failed), Warning(RunWarningCodes.LlmFailover, "Another service answered.")],
        });

        Assert.Single(Occurrences(cut.Markup, OrderResearchLines.Failed));
        Assert.Equal(OrderResearchLines.Failed, cut.Find("[data-testid='order-looked-up-note']").TextContent);
        Assert.Contains("Another service answered.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheLookedUpSentenceTheWarningAboutResearchStays()
    {
        var cut = RenderFull(r => r with { LookedUp = null, Warnings = [Warning(RunWarningCodes.ResearchFailed, OrderResearchLines.Failed)] });

        Assert.Empty(cut.FindAll("[data-testid='order-looked-up']"));
        Assert.Single(Occurrences(cut.Markup, OrderResearchLines.Failed));
    }

    [Fact]
    public void TheLookedUpLinkLeavesThePageSafely()
    {
        var cut = RenderFull();

        var link = cut.Find("[data-testid='order-looked-up-link']");
        Assert.Equal(HonestResultData.LookedUpUrl, link.GetAttribute("href"));
        Assert.Equal("nofollow noopener noreferrer", link.GetAttribute("rel"));
    }

    private static IEnumerable<int> Occurrences(string text, string part)
    {
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            yield return i;
    }
}
