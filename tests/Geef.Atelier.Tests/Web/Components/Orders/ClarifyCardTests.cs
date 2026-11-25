using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.UI.Orders;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The card before writing: what was understood, assumptions that can be changed, up to three questions with clickable
/// answers, "Start writing" (unanswered questions are left to Atelier) and "Just start".
/// </summary>
public sealed class ClarifyCardTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly List<OrderSubmission> _started = [];

    public ClarifyCardTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddScoped<CircuitDataGate>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "One quick question")]
    [InlineData(3, "A few quick questions")]
    public void RendersZeroOneOrThreeQuestions(int count, string? heading)
    {
        var questions = Enumerable.Range(1, count).Select(n => OrderTestData.Question(n)).ToList();
        var (cut, _) = Render(OrderTestData.Clarification(questions: questions));

        Assert.Equal("Before I start", cut.Find("h2").TextContent);
        Assert.Contains("Eine Einladung zum Sommerfest", cut.Find("[data-testid='clarify-understanding']").TextContent);
        Assert.Contains("Here's what I understood:", cut.Markup);
        Assert.Contains("I'm assuming:", cut.Markup);
        Assert.Equal(count, cut.FindAll("[data-testid='question-block']").Count);
        if (heading is null)
        {
            Assert.Empty(cut.FindAll(".clarify-questions-title"));
            Assert.DoesNotContain("Skipping is fine", cut.Markup);
        }
        else
        {
            Assert.Equal(heading, cut.Find(".clarify-questions-title").TextContent);
            Assert.Contains("Skipping is fine – I'll decide what you leave open and go with my assumptions.", cut.Markup);
        }
        Assert.Equal("Start writing", cut.Find("[data-testid='clarify-start']").TextContent.Trim());
        Assert.Equal("Just start", cut.Find("[data-testid='clarify-just-start']").TextContent.Trim());
    }

    [Fact]
    public void TheHeadingGetsTheFocusWhenTheCardAppears()
    {
        var (cut, _) = Render(OrderTestData.Clarification());

        Assert.Equal("-1", cut.Find("h2").GetAttribute("tabindex"));
        var focus = JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
        Assert.IsType<ElementReference>(Assert.Single(focus).Arguments[0]);
    }

    [Fact]
    public void StartWritingWithoutAChoiceSendsNothingSoAtelierDecidesEverything()
    {
        var questions = new[] { OrderTestData.LengthQuestion(), OrderTestData.Question(1), OrderTestData.Question(2, GapTopic.Audience) };
        var (cut, order) = Render(OrderTestData.Clarification(questions: questions));

        cut.Find("[data-testid='clarify-start']").Click();

        var request = Assert.Single(_orders.Answered);
        Assert.Equal(order.OrderId, request.OrderId);
        Assert.Equal("alice", request.RequestingUsername);
        Assert.Empty(request.Answers);
        Assert.Equal(OrderDisplayState.Queued, Assert.Single(_started).State);
    }

    [Fact]
    public void OnlyTheAnsweredQuestionsAreSentTheOpenOnesAreLeftToAtelier()
    {
        var questions = new[] { OrderTestData.LengthQuestion(), OrderTestData.Question(1), OrderTestData.Question(2, GapTopic.Audience) };
        var (cut, _) = Render(OrderTestData.Clarification(questions: questions));

        cut.Find("[data-testid='question-block'][data-id='q2'] [data-testid='question-decide']").Click();
        cut.Find("[data-testid='question-block'][data-id='length'] [data-id='long']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Equal(
            [new OrderAnswer("length", "long", null, false, AnswerOrigin.User), new OrderAnswer("q2", null, null, true, AnswerOrigin.User)],
            Assert.Single(_orders.Answered).Answers);
    }

    [Fact]
    public void AChoiceAndOwnWordsBecomeAnAnswerOfThePerson()
    {
        var questions = new[] { OrderTestData.LengthQuestion(), OrderTestData.Question(1) };
        var (cut, _) = Render(OrderTestData.Clarification(questions: questions));

        cut.Find("[data-testid='question-block'][data-id='length'] [data-id='short']").Click();
        cut.Find("[data-testid='question-block'][data-id='q1'] [data-testid='question-free-toggle']").Click();
        cut.Find("[data-testid='question-block'][data-id='q1'] [data-testid='question-free-text']").Input("  Am 12. Juli ab 16 Uhr  ");
        cut.Find("[data-testid='clarify-start']").Click();

        var answers = Assert.Single(_orders.Answered).Answers;
        Assert.Equal(new OrderAnswer("length", "short", null, false, AnswerOrigin.User), answers[0]);
        Assert.Equal(new OrderAnswer("q1", null, "Am 12. Juli ab 16 Uhr", false, AnswerOrigin.User), answers[1]);
    }

    [Fact]
    public void YouDecideIsSentAsDecideForMe()
    {
        var (cut, _) = Render(OrderTestData.Clarification());

        cut.Find("[data-id='medium']").Click();
        cut.Find("[data-testid='question-decide']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Equal(new OrderAnswer("length", null, null, true, AnswerOrigin.User), Assert.Single(Assert.Single(_orders.Answered).Answers));
    }

    [Fact]
    public void AChangedAssumptionBecomesAnAnswerToItsId()
    {
        var (cut, _) = Render(OrderTestData.Clarification(questions: []));

        cut.Find("[data-testid='clarify-assumption'][data-id='a2'] [data-testid='clarify-assumption-change']").Click();
        var input = cut.Find("[data-testid='clarify-assumption-input']");
        Assert.Equal("Datum und Ort als Platzhalter", input.GetAttribute("value"));
        Assert.Equal("500", input.GetAttribute("maxlength"));
        cut.Find("[data-testid='clarify-assumption-input']").Input("Samstag, 12. Juli, im Innenhof");
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Equal(new OrderAnswer("a2", null, "Samstag, 12. Juli, im Innenhof", false, AnswerOrigin.User), Assert.Single(Assert.Single(_orders.Answered).Answers));
    }

    [Fact]
    public void AnAssumptionOpenedButNotChangedIsNotSent()
    {
        var (cut, _) = Render(OrderTestData.Clarification(questions: []));

        cut.Find("[data-testid='clarify-assumption'][data-id='a1'] [data-testid='clarify-assumption-change']").Click();
        cut.Find("[data-testid='clarify-assumption'][data-id='a2'] [data-testid='clarify-assumption-change']").Click();
        cut.Find("[data-testid='clarify-assumption'][data-id='a2'] [data-testid='clarify-assumption-input']").Input("Anderer Ort");
        cut.Find("[data-testid='clarify-assumption'][data-id='a2'] [data-testid='clarify-assumption-keep']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Empty(Assert.Single(_orders.Answered).Answers);
    }

    [Fact]
    public void TheRevisionGuessCanBeTurnedDown()
    {
        var (cut, _) = Render(OrderTestData.Clarification(questions: [], revisionFileName: "rede.docx"));

        Assert.Contains("I'll improve “rede.docx” and keep its length and voice.", cut.Find("[data-testid='clarify-revision']").TextContent);
        cut.Find("[data-testid='clarify-revision-no']").Click();
        Assert.Contains("write something new", cut.Find("[data-testid='clarify-revision']").TextContent);
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Equal(new OrderAnswer("revision", "no", null, false, AnswerOrigin.User), Assert.Single(Assert.Single(_orders.Answered).Answers));
    }

    [Fact]
    public void WithoutARevisionGuessThereIsNoRevisionLine()
    {
        var (cut, _) = Render(OrderTestData.Clarification());

        Assert.Empty(cut.FindAll("[data-testid='clarify-revision']"));
    }

    [Fact]
    public void JustStartWithoutAnythingDoneSkipsTheQuestions()
    {
        var (cut, order) = Render(OrderTestData.Clarification());

        cut.Find("[data-testid='clarify-just-start']").Click();

        Assert.Equal((order.OrderId, "alice"), Assert.Single(_orders.Skipped));
        Assert.Empty(_orders.Answered);
        Assert.Single(_started);
    }

    [Fact]
    public void JustStartKeepsEverythingThePersonDidAndLeavesTheRestToAtelier()
    {
        var questions = new[] { OrderTestData.LengthQuestion(), OrderTestData.Question(1) };
        var (cut, order) = Render(OrderTestData.Clarification(questions: questions, revisionFileName: "rede.docx"));

        cut.Find("[data-testid='question-block'][data-id='length'] [data-id='short']").Click();
        cut.Find("[data-testid='clarify-revision-no']").Click();
        cut.Find("[data-testid='clarify-assumption'][data-id='a1'] [data-testid='clarify-assumption-change']").Click();
        cut.Find("[data-testid='clarify-assumption'][data-id='a1'] [data-testid='clarify-assumption-input']").Input("Per Sie, eher förmlich");
        cut.Find("[data-testid='clarify-just-start']").Click();

        Assert.Empty(_orders.Skipped);
        var request = Assert.Single(_orders.Answered);
        Assert.Equal((order.OrderId, "alice"), (request.OrderId, request.RequestingUsername));
        Assert.Equal(
            [
                new OrderAnswer("length", "short", null, false, AnswerOrigin.User),
                new OrderAnswer("a1", null, "Per Sie, eher förmlich", false, AnswerOrigin.User),
                new OrderAnswer("revision", "no", null, false, AnswerOrigin.User),
            ],
            request.Answers);
        Assert.Single(_started);
    }

    [Fact]
    public void AnAnswerTheServiceRejectsIsShownAsABannerAndNothingStarts()
    {
        _orders.AnswerError = new OrderAnswerException("This answer option does not belong to the question.");
        var (cut, _) = Render(OrderTestData.Clarification());

        cut.Find("[data-id='short']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        Assert.Contains("This answer option does not belong to the question.", cut.Find("[data-testid='clarify-error']").TextContent);
        Assert.Empty(_started);
        Assert.Equal("true", cut.Find("[data-id='short']").GetAttribute("aria-checked"));
        Assert.False(cut.Find("[data-testid='clarify-start']").HasAttribute("disabled"));
    }

    [Fact]
    public void AnUnexpectedErrorKeepsTheAnswers()
    {
        _orders.SkipError = new InvalidOperationException("db down");
        var (cut, _) = Render(OrderTestData.Clarification());

        cut.Find("[data-testid='clarify-just-start']").Click();

        Assert.Contains("Your answers are still here", cut.Find("[data-testid='clarify-error']").TextContent);
        Assert.DoesNotContain("db down", cut.Markup);
        Assert.Empty(_started);
    }

    [Fact]
    public void QuestionsUnderstandingAndAssumptionsCarryTheLanguageOfTheRequest()
    {
        var (cut, _) = Render(OrderTestData.Clarification(language: "en"));

        Assert.Equal("en", cut.Find("[data-testid='clarify-understanding']").GetAttribute("lang"));
        Assert.Equal("en", cut.Find("[data-testid='question-text']").GetAttribute("lang"));
        Assert.All(cut.FindAll("[data-testid='question-option']"), o => Assert.Equal("en", o.GetAttribute("lang")));
        Assert.All(cut.FindAll(".clarify-assumption-text"), a => Assert.Equal("en", a.GetAttribute("lang")));
        Assert.Null(cut.Find("[data-testid='clarify-card']").GetAttribute("lang"));
    }

    [Fact]
    public void TheCardNamesWhenItStartsOnItsOwn()
    {
        var at = new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero);
        var (cut, _) = Render(OrderTestData.Clarification(autoStartAt: at));

        Assert.Equal($"If you don't answer, I'll start with my assumptions on {at.ToListDate()}.", cut.Find("[data-testid='clarify-autostart-note']").TextContent);
    }

    [Fact]
    public void WithoutAutoStartThereIsNoNote()
    {
        var (cut, _) = Render(OrderTestData.Clarification(autoStartAt: null));

        Assert.Empty(cut.FindAll("[data-testid='clarify-autostart-note']"));
    }

    [Fact]
    public void ALiveReloadWithTheSameOrderKeepsTheChoices()
    {
        var clarification = OrderTestData.Clarification();
        var (cut, order) = Render(clarification);
        cut.Find("[data-id='long']").Click();

        cut.SetParametersAndRender(p => p.Add(c => c.Clarification, clarification with { }));

        Assert.Equal("true", cut.Find("[data-id='long']").GetAttribute("aria-checked"));
        Assert.Equal(order.OrderId, cut.Instance.OrderId);
    }

    private (IRenderedComponent<ClarifyCard> Cut, OrderView Order) Render(ClarificationView clarification)
    {
        var order = _orders.Add("alice", OrderTestData.Asking(clarification));
        var cut = RenderComponent<ClarifyCard>(p => p
            .Add(c => c.OrderId, order.OrderId)
            .Add(c => c.Clarification, clarification)
            .Add(c => c.RequestingUsername, "alice")
            .Add(c => c.OnStarted, EventCallback.Factory.Create<OrderSubmission>(this, s => _started.Add(s))));
        return (cut, order);
    }
}
