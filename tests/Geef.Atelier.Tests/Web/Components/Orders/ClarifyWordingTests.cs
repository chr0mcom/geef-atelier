using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Tests.Web.Components.Profile;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The texts of the clarification are for people who know nothing about how Atelier works inside: none of them
/// contains a word of <see cref="JargonBlocklist.Terms"/>. The check is <see cref="JargonBlocklist.Contains"/>, the same one the
/// gate uses. The questions themselves come from the model and the gate and are removed before the check.
/// </summary>
public sealed class ClarifyWordingTests : TestContext
{
    private readonly FakeOrderService _orders = new();

    public ClarifyWordingTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IProfileService>(new FakeProfileService());
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("alice");
    }

    [Fact]
    public void TheMatcherFindsBlockedWords()
    {
        Assert.True(JargonBlocklist.Contains("Pick a template"));
        Assert.True(JargonBlocklist.Contains("Wer ist die Zielgruppe?"));
        Assert.True(JargonBlocklist.Contains("Start writing\nNone of the prompts"));
        Assert.False(JargonBlocklist.Contains("Start writing"));
    }

    [Fact]
    public void TheCardAndItsStatesUseNoInsiderWords()
    {
        var clarification = OrderTestData.Clarification(
            questions: [OrderTestData.LengthQuestion(), OrderTestData.Question(1)],
            revisionFileName: "notes.docx",
            autoStartAt: OrderTestData.Start.AddDays(1));
        var card = RenderComponent<ClarifyCard>(p => p.Add(c => c.OrderId, Guid.NewGuid()).Add(c => c.Clarification, clarification));
        card.Find("[data-testid='clarify-assumption-change']").Click();
        card.Find("[data-testid='question-free-toggle']").Click();
        _orders.AnswerError = new OrderAnswerException("This answer option does not belong to the question.");
        card.Find("[data-testid='clarify-start']").Click();
        AssertPlain(card.Nodes);

        card.Find("[data-testid='clarify-revision-no']").Click();
        AssertPlain(card.Nodes);
    }

    [Fact]
    public void TheWaitTheSummaryAndAboutMeUseNoInsiderWords()
    {
        AssertPlain(RenderComponent<IntakeWaiting>(p => p.Add(c => c.OrderId, Guid.NewGuid())).Nodes);
        foreach (var outcome in Enum.GetValues<IntakeOutcome>())
        {
            var clarification = OrderTestData.Clarification(outcome: outcome, revisionFileName: "notes.docx",
                answers: [new OrderAnswer("length", null, null, true, AnswerOrigin.Client), new OrderAnswer("revision", "no", null, false, AnswerOrigin.User)]);
            AssertPlain(RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification)).Nodes);
        }
        var about = RenderComponent<AboutMe>();
        about.Find("[data-testid='about-clear']").Click();
        AssertPlain(about.Nodes);
    }

    private static void AssertPlain(INodeList nodes)
    {
        var text = string.Join("\n", nodes.Select(VisibleText));
        var hits = JargonBlocklist.Terms.Where(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.False(JargonBlocklist.Contains(text), $"Insider word ({string.Join(", ", hits)}) in: {text}");
    }

    private static string VisibleText(INode node)
    {
        if (node is IElement element && element.HasAttribute("lang"))
            return string.Empty;
        if (node is IElement { TagName: "TEXTAREA" or "INPUT" })
            return Attributes(node);
        return node.NodeType == NodeType.Text
            ? node.TextContent
            : string.Join(" ", node.ChildNodes.Select(VisibleText)) + " " + Attributes(node);
    }

    private static string Attributes(INode node) =>
        node is IElement element
            ? string.Join(" ", new[] { "aria-label", "placeholder", "title" }.Select(element.GetAttribute).Where(v => v is not null))
            : string.Empty;
}
