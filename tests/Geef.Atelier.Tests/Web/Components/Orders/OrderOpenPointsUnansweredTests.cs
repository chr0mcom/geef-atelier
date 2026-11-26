using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// "Open points" names the questions the person left open before writing, word for word, with a lead that says what the writer was told; for a changed version the lead names the version they were asked before. Without such questions nothing changes.
/// </summary>
public sealed class OrderOpenPointsUnansweredTests : TestContext
{
    private const string Question = "Darf dein Kind an der Klassenfahrt teilnehmen?";

    private static OrderResult Result(IReadOnlyList<string>? unanswered, int? assumptionsVersion = null) =>
        OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered), text: "Liebe Frau Beispiel, vielen Dank für den Elternbrief.") with
        {
            UnansweredQuestions = unanswered,
            AssumptionsVersion = assumptionsVersion,
        };

    private IRenderedComponent<OrderOpenPoints> Render(OrderResult result) =>
        RenderComponent<OrderOpenPoints>(p => p.Add(c => c.Result, result).Add(c => c.Language, "de"));

    [Fact]
    public void The_questions_left_open_are_open_points_with_their_lead()
    {
        var result = Result([Question]);

        var cut = Render(result);

        Assert.True(result.HasOpenPoints());
        Assert.Equal(OrderOpenPoints.UnansweredLeadNow, cut.Find("[data-testid='order-unanswered-lead']").TextContent);
        var item = cut.Find("[data-testid='order-unanswered-question']");
        Assert.Equal(Question, item.TextContent);
        Assert.Equal("de", item.GetAttribute("lang"));
        Assert.Equal(
            "These questions stayed open: you skipped them, left them to me, or I did not ask them. The writer was told not to say yes or no for you on them. Please check how the text handles them:",
            OrderOpenPoints.UnansweredLeadNow);
    }

    [Fact]
    public void For_a_changed_version_the_lead_names_the_version_they_were_asked_before()
    {
        var cut = Render(Result([Question], assumptionsVersion: 1));

        Assert.Equal(
            "Before version 1, these questions stayed open: you skipped them, left them to me, or I did not ask them. A later change may have settled them. Please check how the text handles them:",
            cut.Find("[data-testid='order-unanswered-lead']").TextContent);
        Assert.Equal(Question, cut.Find("[data-testid='order-unanswered-question']").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Without_questions_left_open_there_is_nothing_to_show(bool emptyList)
    {
        var result = Result(emptyList ? [" "] : null);

        Assert.False(result.HasOpenPoints());
        Assert.Empty(Render(result).FindAll("[data-testid='order-unanswered']"));
    }

    [Theory]
    [InlineData("crew")]
    [InlineData("run")]
    [InlineData("briefing")]
    [InlineData("grounding")]
    [InlineData("iteration")]
    [InlineData("convergence")]
    [InlineData("finalizer")]
    [InlineData("manuscript")]
    public void The_new_sentences_use_plain_words(string word)
    {
        foreach (var sentence in new[] { OrderOpenPoints.UnansweredLeadNow, OrderOpenPoints.UnansweredLeadNotedFormat, ClarifyCard.SkipHint })
            Assert.DoesNotMatch($@"(?i)\b{word}\b", sentence);
    }
}
