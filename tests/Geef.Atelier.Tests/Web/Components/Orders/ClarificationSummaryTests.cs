using Bunit;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>After the start: what was understood, the answers and the assumptions, no longer changeable.</summary>
public sealed class ClarificationSummaryTests : TestContext
{
    [Fact]
    public void ShowsUnderstandingAnswersAndAssumptions()
    {
        var clarification = OrderTestData.Clarification(
            questions: [OrderTestData.LengthQuestion(), OrderTestData.Question(1), OrderTestData.Question(2, GapTopic.Audience)],
            answers:
            [
                new OrderAnswer("length", "short", null, false, AnswerOrigin.User),
                new OrderAnswer("q1", "o2", "ab 16 Uhr", false, AnswerOrigin.User),
                new OrderAnswer("q2", null, null, true, AnswerOrigin.User),
                new OrderAnswer("a2", null, "Samstag im Innenhof", false, AnswerOrigin.User),
            ],
            outcome: IntakeOutcome.Answered);

        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification));

        Assert.Equal("You answered before writing started.", cut.Find("[data-testid='clarify-summary-outcome']").TextContent);
        Assert.Contains("Eine Einladung zum Sommerfest", cut.Find("[data-testid='clarify-summary-understanding']").TextContent);
        var answers = cut.FindAll("[data-testid='clarify-summary-answer']").Select(a => a.TextContent).ToList();
        Assert.Equal("Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)", answers[0]);
        Assert.Equal("Frage 1: Wann findet das Fest statt? → Am Samstag – “ab 16 Uhr”", answers[1]);
        Assert.Equal("Frage 2: Wann findet das Fest statt? → left to me", answers[2]);
        var assumptions = cut.FindAll("[data-testid='clarify-summary-assumption']");
        Assert.Equal("Lockerer, herzlicher Ton", assumptions[0].TextContent.Trim());
        Assert.Contains("Samstag im Innenhof", assumptions[1].TextContent);
        Assert.Contains("changed by you", assumptions[1].TextContent);
        Assert.Equal("Datum und Ort als Platzhalter", assumptions[1].QuerySelector("s")!.TextContent);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void AnswersOfTheAssistantAndTheTurnedDownRevisionAreNamed()
    {
        var clarification = OrderTestData.Clarification(
            answers:
            [
                new OrderAnswer("length", "long", null, false, AnswerOrigin.Client),
                new OrderAnswer("revision", "no", null, false, AnswerOrigin.User),
            ],
            revisionFileName: "rede.docx",
            outcome: IntakeOutcome.Answered);

        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification));

        var answers = cut.FindAll("[data-testid='clarify-summary-answer']").Select(a => a.TextContent).ToList();
        Assert.Equal("Wie lang soll der Text ungefähr werden? → Lang (etwa 800 Wörter) (answered by your assistant)", answers[0]);
        Assert.Equal("Improve “rede.docx”? → No, write something new", answers[1]);
    }

    [Theory]
    [InlineData(IntakeOutcome.Clarified, "Nothing needed to be asked")]
    [InlineData(IntakeOutcome.Skipped, "You started without answering")]
    [InlineData(IntakeOutcome.AutoStarted, "no answers within a day")]
    [InlineData(IntakeOutcome.Failed, "your request as written")]
    public void TheOutcomeIsOnePlainSentence(IntakeOutcome outcome, string expected)
    {
        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, OrderTestData.Clarification(outcome: outcome)));

        Assert.Contains(expected, cut.Find("[data-testid='clarify-summary-outcome']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='clarify-summary-answer']"));
    }

    [Fact]
    public void InsideHowItWasMadeTheHeadingIsSmall()
    {
        var cut = RenderComponent<ClarificationSummary>(p => p
            .Add(c => c.Clarification, OrderTestData.Clarification(outcome: IntakeOutcome.Clarified))
            .Add(c => c.Inline, true));

        Assert.Equal("Before writing", cut.Find("h3").TextContent);
        Assert.Empty(cut.FindAll("h2"));
        Assert.Contains("clarify-summary--inline", cut.Find("[data-testid='clarify-summary']").ClassList);
    }
}
