using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The result page shows each point once without ever losing a point that says something else (open point of stage 3): an
/// assumption of the writer that only adds a leading article to one of the clarifier is shown once, any other difference keeps
/// both; a contradiction that only repeats a wrong total inside its own passage is told once by the total; an open point of the
/// writer about the same total stays complete and is shown with that sentence instead of on its own.
/// </summary>
public sealed class OrderResultDuplicatesTests
{
    private static OrderResult Base(params string[] clarifier) =>
        new(Guid.NewGuid(), OrderStateResolver.Resolve(OrderState.Ready, null), Guid.NewGuid(), 1, "Liebe Eltern", 2, [], [],
            [new OrderSourceUsageView("notizen.txt", SourceUsageMode.FullText, 400, null)], [], null,
            clarifier.Select((t, i) => new IntakeAssumption($"a{i + 1}", GapTopic.Tone, t, AssumptionConfidence.Sure)).ToList());

    private static RunReport Report(WriterNotes notes, SourceArithmeticResult? arithmetic = null, params FactFinding[] conflicts) =>
        new(2, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            [new RunReportSource(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400)], 1, 1, null, [], WriterNotesStatus.Ok, notes, [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, conflicts.Length > 0 ? FactCheckState.OpenPoints : FactCheckState.Checked,
                [], conflicts, [], 1, "m", false, "fact-check-1"),
            arithmetic, []);

    private static WriterNotes Notes(IReadOnlyList<string>? assumptions = null, IReadOnlyList<string>? openPoints = null) =>
        new(assumptions ?? [], openPoints ?? [], []);

    [Fact]
    public void The_parent_letter_shows_each_assumption_once_and_keeps_every_other_wording()
    {
        var result = Base("Eltern werden mit Sie angesprochen.", "Freundlich, herzlich, aber klar und sachlich.")
            .WithReport(Report(Notes(
            [
                "Die Eltern werden mit Sie angesprochen.",
                "Der Ton ist freundlich und herzlich, aber sachlich gehalten.",
                "Die Informationen sind mit fett gesetzten Stichworten gegliedert.",
            ])));

        Assert.Equal(
            [
                "Eltern werden mit Sie angesprochen.",
                "Freundlich, herzlich, aber klar und sachlich.",
                "Der Ton ist freundlich und herzlich, aber sachlich gehalten.",
                "Die Informationen sind mit fett gesetzten Stichworten gegliedert.",
            ],
            result.AllAssumptions());
    }

    [Theory]
    [InlineData("Eltern werden mit Sie angesprochen.", "Die Eltern werden mit Sie angesprochen.", true)]
    [InlineData("Die Eltern werden mit Sie angesprochen", "eltern werden mit  Sie angesprochen.", true)]
    [InlineData("The parents are addressed formally.", "Parents are addressed formally", true)]
    [InlineData("Das Schreiben ist kurz.", "Schreiben ist kurz!", true)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Keine Eltern werden mit Sie angesprochen.", false)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Nur Eltern werden mit Sie angesprochen.", false)]
    [InlineData("Die Lehrerin dankt der Mutter.", "Der Lehrerin dankt die Mutter.", false)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Eltern werden mit Sie angesprochen, Kinder mit du.", false)]
    [InlineData("Der Ton ist freundlich, aber sachlich.", "Freundlich, herzlich, aber klar und sachlich.", false)]
    [InlineData("Die Eltern werden mit Sie angesprochen.", "Die Eltern werden mit du angesprochen.", false)]
    public void Two_assumptions_count_as_one_only_when_they_differ_in_nothing_but_a_leading_article(string first, string second, bool same)
    {
        Assert.Equal(same, string.Equals(OrderResultRules.SameAssumptionKey(first), OrderResultRules.SameAssumptionKey(second), StringComparison.OrdinalIgnoreCase));

        var shown = Base(first).WithReport(Report(Notes([second]))).AllAssumptions();

        Assert.Equal(same ? [first] : [first, second], shown);
    }

    [Fact]
    public void Repeats_among_the_assumptions_of_the_clarifier_are_shown_once_too()
    {
        var result = Base("Die Anrede ist förmlich.", "Anrede ist förmlich");

        Assert.Equal(["Die Anrede ist förmlich."], result.AllAssumptions());
    }

    private const string Notes62 = "Treffen am Freitag\n- Bastelkosten: Papier 22 €, Farben 31 €, Kleber 19 €, zusammen 62 €\n- Plätzchen ohne Nüsse";

    private static readonly FactFinding ItemsAgainstTotal = new("f9", FactFindingKind.SourceConflict, null, "x", null,
        "Papier 22 €, Farben 31 €, Kleber 19 €", "zusammen 62 €", "notizen.txt", null, false, QuoteMatchKind.NotFound,
        MaterialPart.Files, MaterialPart.Files, "notizen.txt");

    private const string WriterOnTotal =
        "In den Notizen ergeben Papier 22 €, Farben 31 € und Kleber 19 € zusammen 72 €, als Summe steht dort aber 62 €; der Gesamtbetrag ist deshalb als Platzhalter offen gelassen.";

    private const string WriterOther = "Es ist nicht angegeben, bei wem das Geld abgegeben wird.";

    [Fact]
    public void A_wrong_total_is_told_once_and_the_open_point_of_the_writer_about_it_stays_complete_beside_it()
    {
        var arithmetic = SourceArithmeticCheck.Check([("S1", "notizen.txt", Notes62)]);
        var result = Base().WithReport(Report(Notes(openPoints: [WriterOnTotal, WriterOther]), arithmetic, ItemsAgainstTotal));

        var total = Assert.Single(result.SourceIssues!);
        Assert.StartsWith("Your file \"notizen.txt\" gives 62 € as the total, but 22 € + 31 € + 19 € = 72 €.", total, StringComparison.Ordinal);
        Assert.Equal([new OrderSourceIssueNote(total, WriterOnTotal)], result.SourceIssueNotes);
        Assert.Equal([WriterOnTotal], result.NotesOn(total));
        Assert.Equal([WriterOther], result.WriterOpenPointsOnTheirOwn());
        Assert.Equal([WriterOnTotal, WriterOther], result.WriterOpenPoints);
        Assert.True(result.HasOpenPoints());
    }

    [Fact]
    public void An_open_point_of_the_writer_that_names_only_one_figure_of_the_total_stays_on_its_own()
    {
        var arithmetic = SourceArithmeticCheck.Check([("S1", "notizen.txt", Notes62)]);
        const string onlyStated = "Die Summe von 62 € sollte geprüft werden.";

        var result = Base().WithReport(Report(Notes(openPoints: [onlyStated]), arithmetic));

        Assert.Empty(result.SourceIssueNotes!);
        Assert.Equal([onlyStated], result.WriterOpenPointsOnTheirOwn());
    }

    [Fact]
    public void An_open_point_paired_with_a_sentence_that_is_not_shown_stays_on_its_own()
    {
        var result = Base() with
        {
            WriterOpenPoints = [WriterOnTotal],
            SourceIssues = [],
            SourceIssueNotes = [new OrderSourceIssueNote("Your file \"notizen.txt\" gives 62 € as the total.", WriterOnTotal)],
        };

        Assert.Equal([WriterOnTotal], result.WriterOpenPointsOnTheirOwn());
    }

    [Fact]
    public void Without_a_wrong_total_the_open_points_of_the_writer_are_unchanged()
    {
        var result = Base().WithReport(Report(Notes(openPoints: [WriterOnTotal, WriterOther])));

        Assert.Empty(result.SourceIssueNotes!);
        Assert.Equal([WriterOnTotal, WriterOther], result.WriterOpenPointsOnTheirOwn());
    }
}
