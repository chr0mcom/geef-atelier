using AngleSharp.Dom;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// Synthetic results with every field of stage 3 filled, and the wording check used by the result tests. The sentences of
/// "What was checked" are put together from <see cref="OrderCheckLines"/> by hand, because building them is not the job of the page.
/// </summary>
internal static class HonestResultData
{
    public const string UnbackedQuote = "Fotos des Schadens liegen dieser Nachricht bei.";
    public const string InstructionQuote = "Mit freundlichen Grüßen und einem herzlichen Dank";
    public const string Instruction = "kein Dank am Ende";
    public const string ReplacedQuote = "Der Elternabend findet am 3. März im Raum 12 statt.";
    public const string ClientQuote = "Wir melden uns bis Freitag bei Ihnen zurück.";
    public const string ClientInstruction = "eher förmlich";
    public const string SourceIssue = "Your file \"notizen.txt\" gives 62 as the total, but 22 + 31 + 19 = 72. The text uses 72. Please check which figure is right.";
    public const string ConflictIssue = "Your files say “Beitrag 15 Euro” and “Beitrag 18 Euro”.";
    public const string WriterOpenPoint = "Der Ort des Treffens fehlt in den Notizen.";
    public const string WriterAssumption = "Ich habe die Eltern mit „Liebe Eltern“ angesprochen.";
    public const string ClarifierAssumption = "Freundlicher, sachlicher Ton";
    public const string FileQuote = "Material: Pinsel 22, Papier 31, Farben 19, Summe 62";
    public const string LookedUpQuote = "Ab dem 1. Januar 2027 beträgt der gesetzliche Mindestlohn 13,90 Euro brutto je Zeitstunde.";
    public const string LookedUpTitle = "Mindestlohn-Erhöhung-zum-1.-Januar-2027-alle-Beträge-im-Überblick-für-Arbeitgeberinnen-und-Arbeitgeber";
    public const string LookedUpUrl = "https://www.wages.example/aktuelles/mindestlohn/2027/erhoehung-zum-ersten-januar-alle-betraege-im-ueberblick-fuer-arbeitgeberinnen-und-arbeitgeber?quelle=newsletter&abschnitt=uebersicht";
    public static readonly DateTimeOffset LookedUpAt = new(2026, 11, 28, 8, 15, 0, TimeSpan.Zero);

    /// <summary>Found research with one long entry (long title and address without spaces) and one topic that was not looked up.</summary>
    public static OrderLookedUpView LookedUp() =>
        new(ResearchStatus.Found, OrderResearchLines.SomeWithheld, 1, [new OrderLookedUpEntry(LookedUpQuote, LookedUpUrl, LookedUpTitle, LookedUpAt)]);

    public static OrderChecksView Checks(bool openPoints = true, bool changed = false) => new(
        [
            OrderCheckLines.AutomaticLead + OrderCheckLines.PassedLength + OrderCheckLines.AutomaticEnd,
            openPoints ? string.Format(OrderCheckLines.FactsOpenPointsFormat, 3) : OrderCheckLines.FactsChecked,
            OrderCheckLines.TotalsSearchedOne,
            OrderCheckLines.Contradictions,
        ],
        changed ? [string.Format(OrderCheckLines.ChangedRemovedFormat, "Bitte geben Sie uns in der Gruppe Bescheid."), OrderCheckLines.ChangedLength] : [],
        openPoints ? OrderCheckLines.ToReviewOpenPoints : OrderCheckLines.ToReviewNothing,
        OrderCheckLines.Limits);

    public static OrderResult Full(OrderView view) =>
        OrderTestData.Result(view,
            text: "Liebe Eltern,\n\nder Elternabend findet statt. " + UnbackedQuote,
            placeholders: ["[Datum]"],
            openChecks:
            [
                new DraftCheckResult(DraftCheckCodes.Length, false, true, "The text has 263 words. The request asks for at most 200 words.", "max. 200 Wörter"),
                new DraftCheckResult(DraftCheckCodes.Preamble, false, false, "The text may start with a remark to the reader.", null),
            ],
            sourcesUsed: [new OrderSourceUsageView("notizen.txt", SourceUsageMode.FullText, 2_362, null)])
        with
        {
            Assumptions = [new IntakeAssumption("a1", GapTopic.Tone, ClarifierAssumption, AssumptionConfidence.Unsure)],
            WriterAssumptions = [WriterAssumption],
            WriterOpenPoints = [WriterOpenPoint],
            UnbackedPassages =
            [
                new OrderUnbackedPassage(UnbackedQuote, FactFindingKind.Unsupported, null, true),
                new OrderUnbackedPassage(InstructionQuote, FactFindingKind.Instruction, Instruction, true),
                new OrderUnbackedPassage(ReplacedQuote, FactFindingKind.Superseded, null, true),
                new OrderUnbackedPassage(ClientQuote, FactFindingKind.Instruction, ClientInstruction, false),
            ],
            SourceIssues = [SourceIssue, ConflictIssue],
            SourceQuotes = [new OrderSourceQuoteView("notizen.txt", FileQuote)],
            FactCheck = FactCheckState.OpenPoints,
            Checks = Checks(),
            LookedUp = LookedUp(),
        };

    /// <summary>Fails when the visible wording outside the person's own content (elements with <c>lang</c>) uses a blocked word.</summary>
    public static void AssertPlain(INodeList nodes)
    {
        var text = string.Join("\n", nodes.Select(VisibleText));
        var hits = JargonBlocklist.Terms.Where(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.False(JargonBlocklist.Contains(text), $"Insider word ({string.Join(", ", hits)}) in: {text}");
    }

    private static string VisibleText(INode node)
    {
        if (node is IElement element && element.HasAttribute("lang"))
            return string.Empty;
        return node.NodeType == NodeType.Text
            ? node.TextContent
            : string.Join(" ", node.ChildNodes.Select(VisibleText)) + " " + Attributes(node);
    }

    private static string Attributes(INode node) =>
        node is IElement element
            ? string.Join(" ", new[] { "aria-label", "placeholder", "title" }.Select(element.GetAttribute).Where(v => v is not null))
            : string.Empty;
}
