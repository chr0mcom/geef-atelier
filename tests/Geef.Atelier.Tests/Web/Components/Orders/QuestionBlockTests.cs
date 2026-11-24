using Bunit;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Web.Components.UI.Orders;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>One question: clickable options as a radio group with arrow keys, "You decide" and optional own words with a counter.</summary>
public sealed class QuestionBlockTests : TestContext
{
    private readonly List<OrderAnswer?> _changes = [];

    public QuestionBlockTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void OptionsFormARadioGroupLabelledByTheQuestionInItsLanguage()
    {
        var cut = Render(OrderTestData.LengthQuestion());

        var group = cut.Find("[role='radiogroup']");
        var textId = cut.Find("[data-testid='question-text']").Id;
        Assert.Equal(textId, group.GetAttribute("aria-labelledby"));
        Assert.Equal("de", cut.Find("[data-testid='question-text']").GetAttribute("lang"));
        var options = cut.FindAll("[data-testid='question-option']");
        Assert.Equal(new[] { "short", "medium", "long" }, options.Select(o => o.GetAttribute("data-id")));
        Assert.All(options, o => Assert.Equal("radio", o.GetAttribute("role")));
        Assert.All(options, o => Assert.Equal("false", o.GetAttribute("aria-checked")));
        Assert.Equal(new[] { "0", "-1", "-1" }, options.Select(o => o.GetAttribute("tabindex")));
        Assert.Equal("Kurz (etwa 150 Wörter)", options[0].TextContent);
    }

    [Fact]
    public void ClickingAnOptionChecksItAndReportsTheAnswer()
    {
        var cut = Render(OrderTestData.LengthQuestion());

        cut.Find("[data-id='medium']").Click();

        var answer = Assert.Single(_changes)!;
        Assert.Equal(new OrderAnswer("length", "medium", null, false, AnswerOrigin.User), answer);
        Assert.Equal("true", cut.Find("[data-id='medium']").GetAttribute("aria-checked"));
        Assert.Equal("0", cut.Find("[data-id='medium']").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[data-id='short']").GetAttribute("tabindex"));
    }

    [Theory]
    [InlineData("ArrowRight", "short", "medium")]
    [InlineData("ArrowDown", "long", "short")]
    [InlineData("ArrowLeft", "short", "long")]
    [InlineData("ArrowUp", "medium", "short")]
    [InlineData("End", "short", "long")]
    [InlineData("Home", "long", "short")]
    public void ArrowKeysMoveTheChoiceAndTheFocus(string key, string from, string expected)
    {
        var cut = Render(OrderTestData.LengthQuestion());
        cut.Find($"[data-id='{from}']").Click();

        cut.Find($"[data-id='{from}']").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, _changes[^1]!.OptionId);
        Assert.Equal("true", cut.Find($"[data-id='{expected}']").GetAttribute("aria-checked"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void OtherKeysChangeNothing()
    {
        var cut = Render(OrderTestData.LengthQuestion());

        cut.Find("[data-id='short']").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(_changes);
    }

    [Fact]
    public void YouDecideClearsAChosenOptionAndCanBeTakenBack()
    {
        var cut = Render(OrderTestData.LengthQuestion());
        cut.Find("[data-id='short']").Click();

        cut.Find("[data-testid='question-decide']").Click();

        Assert.Equal(new OrderAnswer("length", null, null, true, AnswerOrigin.User), _changes[^1]);
        Assert.All(cut.FindAll("[data-testid='question-option']"), o => Assert.Equal("false", o.GetAttribute("aria-checked")));
        Assert.Equal("true", cut.Find("[data-testid='question-decide']").GetAttribute("aria-pressed"));

        cut.Find("[data-testid='question-decide']").Click();

        Assert.Null(_changes[^1]);
        Assert.Equal("false", cut.Find("[data-testid='question-decide']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void OwnWordsAreOptionalAndCountedUpToTheLimit()
    {
        var cut = Render(OrderTestData.LengthQuestion());
        Assert.Empty(cut.FindAll("[data-testid='question-free-text']"));

        cut.Find("[data-testid='question-free-toggle']").Click();
        var field = cut.Find("[data-testid='question-free-text']");
        Assert.Equal("500", field.GetAttribute("maxlength"));
        Assert.Equal("0 / 500", cut.Find("[data-testid='question-free-count']").TextContent);

        cut.Find("[data-testid='question-free-text']").Input("Etwa eine Seite");

        Assert.Equal(new OrderAnswer("length", null, "Etwa eine Seite", false, AnswerOrigin.User), _changes[^1]);
        Assert.Equal("15 / 500", cut.Find("[data-testid='question-free-count']").TextContent);

        cut.Find("[data-testid='question-free-text']").Input(new string('x', 600));

        Assert.Equal(500, _changes[^1]!.FreeText!.Length);
        Assert.Equal("500 / 500", cut.Find("[data-testid='question-free-count']").TextContent);
        Assert.Contains("is-full", cut.Find("[data-testid='question-free-count']").ClassList);
    }

    [Fact]
    public void OwnWordsKeepTheChosenOptionAndEndYouDecide()
    {
        var cut = Render(OrderTestData.LengthQuestion());
        cut.Find("[data-id='long']").Click();
        cut.Find("[data-testid='question-free-toggle']").Click();

        cut.Find("[data-testid='question-free-text']").Input("mit Programm");

        Assert.Equal(new OrderAnswer("length", "long", "mit Programm", false, AnswerOrigin.User), _changes[^1]);

        cut.Find("[data-testid='question-decide']").Click();
        Assert.Empty(cut.FindAll("[data-testid='question-free-text']"));
        cut.Find("[data-testid='question-free-toggle']").Click();
        cut.Find("[data-testid='question-free-text']").Input("doch lieber kurz");

        Assert.Equal(new OrderAnswer("length", null, "doch lieber kurz", false, AnswerOrigin.User), _changes[^1]);
        Assert.Equal("false", cut.Find("[data-testid='question-decide']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void WithoutFreeTextThereIsNoToggle()
    {
        var cut = Render(OrderTestData.Question(1) with { AllowFreeText = false });

        Assert.Empty(cut.FindAll("[data-testid='question-free-toggle']"));
        Assert.Equal(2, cut.FindAll("[data-testid='question-option']").Count);
    }

    [Fact]
    public void AGivenAnswerIsShown()
    {
        var cut = Render(OrderTestData.LengthQuestion(), new OrderAnswer("length", "short", "knapp", false, AnswerOrigin.User));

        Assert.Equal("true", cut.Find("[data-id='short']").GetAttribute("aria-checked"));
        Assert.Equal("knapp", cut.Find("[data-testid='question-free-text']").GetAttribute("value"));
    }

    private IRenderedComponent<QuestionBlock> Render(ClarifyQuestion question, OrderAnswer? answer = null) =>
        RenderComponent<QuestionBlock>(p => p
            .Add(c => c.Question, question)
            .Add(c => c.Language, "de")
            .Add(c => c.Answer, answer)
            .Add(c => c.AnswerChanged, EventCallback.Factory.Create<OrderAnswer?>(this, a => _changes.Add(a))));
}
