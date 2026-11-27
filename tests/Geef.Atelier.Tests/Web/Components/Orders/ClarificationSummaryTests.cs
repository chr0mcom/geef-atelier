using Geef.Atelier.Application.Orders;
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
        Assert.Equal("Frage 2: Wann findet das Fest statt? → left open", answers[2]);
        var assumptions = cut.FindAll("[data-testid='clarify-summary-assumption']");
        Assert.Equal("Lockerer, herzlicher Ton", assumptions[0].TextContent.Trim());
        Assert.Contains("Samstag im Innenhof", assumptions[1].TextContent);
        Assert.Contains("changed by you", assumptions[1].TextContent);
        Assert.Equal("Datum und Ort als Platzhalter", assumptions[1].QuerySelector("s")!.TextContent);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void You_decide_on_the_length_question_keeps_left_to_me_and_on_other_questions_reads_left_open()
    {
        var clarification = OrderTestData.Clarification(
            questions: [OrderTestData.LengthQuestion(), OrderTestData.Question(1)],
            answers: [new OrderAnswer("length", null, null, true, AnswerOrigin.User), new OrderAnswer("q1", null, null, true, AnswerOrigin.User)],
            outcome: IntakeOutcome.Answered);

        var answers = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification))
            .FindAll("[data-testid='clarify-summary-answer']").Select(a => a.TextContent).ToList();

        Assert.Equal("Wie lang soll der Text ungefähr werden? → left to me", answers[0]);
        Assert.Equal("Frage 1: Wann findet das Fest statt? → left open", answers[1]);
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
    [InlineData(IntakeOutcome.AutoStarted, "no answers in time")]
    [InlineData(IntakeOutcome.Failed, "your request as written")]
    public void TheOutcomeIsOnePlainSentence(IntakeOutcome outcome, string expected)
    {
        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, OrderTestData.Clarification(outcome: outcome)));

        Assert.Contains(expected, cut.Find("[data-testid='clarify-summary-outcome']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='clarify-summary-answer']"));
    }

    [Fact]
    public void ASkipWhileTheRequestWasStillReadDoesNotClaimAssumptions()
    {
        var clarification = new ClarificationView(IntakeOutcome.Skipped, null, null, null, [], [], [], null, null, null);

        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification));

        var text = cut.Find("[data-testid='clarify-summary-outcome']").TextContent;
        Assert.Equal(OrderDisplayText.SkippedBeforeReadingText, text);
        Assert.DoesNotContain("assumption", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASkipWithoutAssumptionsDoesNotClaimThem()
    {
        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, OrderTestData.Clarification(assumptions: [], outcome: IntakeOutcome.Skipped)));

        Assert.Equal(OrderDisplayText.SkippedWithoutAssumptionsText, cut.Find("[data-testid='clarify-summary-outcome']").TextContent);
    }

    [Theory]
    [InlineData("no", AnswerOrigin.Client, "Improve “rede.docx”? → No, write something new (answered by your assistant)")]
    [InlineData("yes", AnswerOrigin.Client, "Improve “rede.docx”? → Yes, improve it (answered by your assistant)")]
    [InlineData("yes", AnswerOrigin.User, "Improve “rede.docx”? → Yes, improve it")]
    [InlineData("no", AnswerOrigin.Form, "Improve “rede.docx”? → No, write something new (chosen in a form of your assistant)")]
    public void TheRevisionAnswerShowsWhoGaveIt(string option, AnswerOrigin origin, string expected)
    {
        var clarification = OrderTestData.Clarification(questions: [], answers: [new OrderAnswer("revision", option, null, false, origin)],
            revisionFileName: "rede.docx", outcome: IntakeOutcome.Answered);

        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification));

        Assert.Equal(expected, Assert.Single(cut.FindAll("[data-testid='clarify-summary-answer']")).TextContent);
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

    [Fact]
    public void BelowAResultWithItsOwnAssumptionsOnlyTheChangedOnesAreRepeated()
    {
        var clarification = OrderTestData.Clarification(
            answers: [new OrderAnswer("a2", null, "Samstag im Innenhof", false, AnswerOrigin.User)],
            outcome: IntakeOutcome.Answered);

        var cut = RenderComponent<ClarificationSummary>(p => p.Add(c => c.Clarification, clarification).Add(c => c.Inline, true).Add(c => c.OnlyCorrectedAssumptions, true));

        var assumption = Assert.Single(cut.FindAll("[data-testid='clarify-summary-assumption']"));
        Assert.Contains("Samstag im Innenhof", assumption.TextContent);
        Assert.Contains("Assumptions you changed:", cut.Markup);
        Assert.DoesNotContain("Lockerer, herzlicher Ton", cut.Markup);
    }
}
