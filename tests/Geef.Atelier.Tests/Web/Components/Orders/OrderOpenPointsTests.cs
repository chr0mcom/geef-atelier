using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// "Open points": every group with its fixed lead sentence, in the order of what matters most to the person; quotes from the
/// text are shown word for word, never the reason the check gave in its own words.
/// </summary>
public sealed class OrderOpenPointsTests : TestContext
{
    private static readonly string[] Leads =
    [
        "Fill these in before you send the text:",
        "The text may not yet do everything you asked for:",
        "These passages may not be backed by your request or your files. Please check them:",
        "These passages may not follow what you asked for:",
        "This passage uses something your files replace or drop:",
        "Please check these figures and statements:",
        "Still open:",
        "Please also look at this:",
    ];

    private static OrderResult Full() => HonestResultData.Full(OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints));

    private IRenderedComponent<OrderOpenPoints> Render(OrderResult result, string? language = "de") =>
        RenderComponent<OrderOpenPoints>(p => p.Add(c => c.Result, result).Add(c => c.Language, language));

    [Fact]
    public void EveryGroupAppearsInTheOrderOfWhatMattersMost()
    {
        var cut = Render(Full());

        var leads = cut.FindAll("[data-testid='order-open-points'] > p").Select(e => e.TextContent).ToList();
        Assert.Equal(Leads, leads);
    }

    [Fact]
    public void PassagesAreQuotedWordForWordWithWhatWasAsked()
    {
        var cut = Render(Full());

        Assert.Equal(HonestResultData.UnbackedQuote, cut.Find("[data-testid='order-unbacked'] q").TextContent);
        var against = cut.Find("[data-testid='order-against-instruction'] [data-testid='order-passage']");
        Assert.Equal(HonestResultData.InstructionQuote, against.QuerySelector("q")!.TextContent);
        Assert.Equal($"You asked: “{HonestResultData.Instruction}”", against.QuerySelector(".order-passage-asked")!.TextContent);
        Assert.Equal(HonestResultData.ReplacedQuote, cut.Find("[data-testid='order-replaced'] q").TextContent);
        Assert.Equal(new[] { HonestResultData.SourceIssue, HonestResultData.ConflictIssue },
            cut.FindAll("[data-testid='order-source-issue']").Select(e => e.TextContent));
        Assert.Equal(HonestResultData.WriterOpenPoint, cut.Find("[data-testid='order-writer-open-point']").TextContent);
        Assert.All(cut.FindAll("q.order-quote"), q => Assert.Equal("de", q.GetAttribute("lang")));
    }

    [Fact]
    public void PassagesThatNeverBlockAreOnlyHints()
    {
        var cut = Render(Full());

        var hints = cut.FindAll("[data-testid='order-hint']");
        Assert.Equal(2, hints.Count);
        var client = Assert.Single(hints, h => h.GetAttribute("data-kind") == "against-instruction");
        Assert.Equal(HonestResultData.ClientQuote, client.QuerySelector("q")!.TextContent);
        Assert.Contains(HonestResultData.ClientInstruction, client.TextContent);
        Assert.DoesNotContain(HonestResultData.ClientQuote, cut.Find("[data-testid='order-against-instruction']").TextContent);
    }

    [Fact]
    public void WithoutThePagesLookedUpTheProblemsInTheMaterialAreWhatThePersonGave()
    {
        var cut = Render(Full() with { LookedUp = null });

        Assert.Contains("Please check these figures and statements in what you gave:", cut.FindAll("[data-testid='order-open-points'] > p").Select(e => e.TextContent));
    }

    [Fact]
    public void APassageThePagesLookedUpSayOtherwiseIsAHintWithItsOwnSentenceNeverAMatterOfTheFiles()
    {
        const string quote = "Der Jahresbeitrag beträgt 30 Euro.";
        var result = OrderTestData.Result(OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints)) with
        {
            UnbackedPassages = [new OrderUnbackedPassage(quote, FactFindingKind.Superseded, null, false, null, LookedUp: true)],
        };

        var cut = Render(result);

        var hint = cut.Find("[data-testid='order-hint']");
        Assert.Equal("differs-from-looked-up", hint.GetAttribute("data-kind"));
        Assert.Equal(quote, hint.QuerySelector("q")!.TextContent);
        Assert.Equal(OrderResearchLines.DiffersFromLookedUp, hint.QuerySelector("[data-testid='order-hint-looked-up']")!.TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-replaced']"));
        Assert.DoesNotContain("your files", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceConflictIsNeverShownAsAPassageOfTheText()
    {
        var result = OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with
        {
            UnbackedPassages = [new OrderUnbackedPassage("Beitrag 15 Euro je Kind für das Material", FactFindingKind.SourceConflict, null, false)],
        };

        var cut = Render(result);

        Assert.Empty(cut.FindAll("[data-testid='order-open-points']"));
    }

    [Fact]
    public void OnlyFixedSentencesAndQuotesAreShownNeverAReasonFromTheCheck()
    {
        var result = Full();
        var cut = Render(result);

        var allowed = Leads
            .Concat(result.Placeholders)
            .Concat(result.OpenChecks.Select(c => c.Message))
            .Concat(result.UnbackedPassages!.Select(p => p.Quote))
            .Concat(result.SourceIssues!)
            .Concat(result.WriterOpenPoints!)
            .Append("Open points")
            .Append($"You asked: “{HonestResultData.Instruction}”")
            .Append($"Asked for: “{HonestResultData.ClientInstruction}”")
            .ToList();
        var rest = cut.Find("[data-testid='order-open-points']").TextContent;
        foreach (var piece in allowed.OrderByDescending(p => p.Length))
            rest = rest.Replace(piece, string.Empty, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(rest), $"Unexpected text in the open points: {rest.Trim()}");
    }

    [Fact]
    public void ResultsWithoutTheNewFieldsLookAsBefore()
    {
        var cut = Render(OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered), placeholders: ["[Datum]"]));

        Assert.Equal(new[] { "Fill these in before you send the text:" }, cut.FindAll("[data-testid='order-open-points'] > p").Select(e => e.TextContent));
        Assert.Empty(cut.FindAll("q"));
    }

    [Fact]
    public void WithNothingOpenThereIsNoBlock()
    {
        var cut = Render(OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with
        {
            UnbackedPassages = [], SourceIssues = [], WriterOpenPoints = [" "],
        });

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void TheBlockUsesNoInsiderWords() => HonestResultData.AssertPlain(Render(Full()).Nodes);

    [Fact]
    public void AnInstructionFromTheAboutMeDetailsIsNamedAsSuchAndHiddenWhenTheViewerMayNotSeeIt()
    {
        var shown = Full() with { UnbackedPassages = [new OrderUnbackedPassage(HonestResultData.InstructionQuote, FactFindingKind.Instruction, "Ich duze nie", true, InstructionOrigin.Memory)] };
        var hidden = Full() with { UnbackedPassages = [new OrderUnbackedPassage(HonestResultData.InstructionQuote, FactFindingKind.Instruction, null, true, InstructionOrigin.Memory)] };

        var owner = Render(shown).Find("[data-origin='about-me']").TextContent;
        var other = Render(hidden).Find("[data-origin='about-me']").TextContent;

        Assert.Equal("Your About me details say: “Ich duze nie”", owner);
        Assert.DoesNotContain("You asked", Render(shown).Markup);
        Assert.Equal("This may go against the About me details of the person who ordered the text; they are hidden here.", other);
        Assert.Contains("These passages may not follow what you asked for:", Render(shown).Markup);
        Assert.Contains("These passages may not follow what was asked for:", Render(hidden).Markup);
        Assert.DoesNotContain("what you asked for", Render(hidden).Markup);
    }

    [Fact]
    public void NotesThatCouldNotBeShownAreCountedUnderStillOpen()
    {
        var result = OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with { NotesNotShown = OrderResultReport.NotesNotShownOne };

        var cut = Render(result);

        Assert.Equal(OrderResultReport.NotesNotShownOne, cut.Find("[data-testid='order-notes-not-shown']").TextContent);
        Assert.Contains("Still open:", cut.Markup);
    }

    [Fact]
    public void AnOpenPointOfTheWriterAboutAWrongTotalIsShownWithItsSentenceAndNotAgainUnderStillOpen()
    {
        const string onTotal = "In den Notizen ergeben 22 + 31 + 19 zusammen 72, als Summe steht dort aber 62; der Betrag ist offen gelassen.";
        var result = Full() with
        {
            WriterOpenPoints = [onTotal, HonestResultData.WriterOpenPoint],
            SourceIssueNotes = [new OrderSourceIssueNote(HonestResultData.SourceIssue, onTotal)],
        };

        var cut = Render(result);

        var issues = cut.FindAll("[data-testid='order-source-issue']");
        var note = Assert.Single(issues[0].QuerySelectorAll("[data-testid='order-source-issue-note']"));
        Assert.Equal(OrderOpenPoints.WriterNoteLead + " " + onTotal, note.TextContent.Trim());
        Assert.Equal("de", note.QuerySelector("[lang]")!.GetAttribute("lang"));
        Assert.StartsWith(HonestResultData.SourceIssue, issues[0].TextContent.Trim(), StringComparison.Ordinal);
        Assert.Empty(issues[1].QuerySelectorAll("[data-testid='order-source-issue-note']"));
        Assert.Equal(new[] { HonestResultData.WriterOpenPoint }, cut.FindAll("[data-testid='order-writer-open-point']").Select(e => e.TextContent));
        Assert.Single(cut.FindAll("li"), li => li.TextContent.Contains(onTotal, StringComparison.Ordinal));
        HonestResultData.AssertPlain(cut.Nodes);
    }

    [Fact]
    public void WhenEveryOpenPointOfTheWriterIsShownWithATotalThereIsNoStillOpenGroup()
    {
        const string onTotal = "Summe 62 statt 72.";
        var result = Full() with
        {
            WriterOpenPoints = [onTotal],
            SourceIssueNotes = [new OrderSourceIssueNote(HonestResultData.SourceIssue, onTotal)],
        };

        var cut = Render(result);

        Assert.DoesNotContain("Still open:", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='order-source-issue-note']"));
    }
}
