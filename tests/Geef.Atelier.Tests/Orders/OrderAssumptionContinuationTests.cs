using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Mcp.Models;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// An assumption that another continues word for word is shown once (contract stage 5, 4.1 d): only when the shorter one is the
/// beginning of the longer one and a punctuation mark follows there; the longer one is shown where the first stood, an assumption
/// of the clarifier is never left out, and any other wording, also one that says the same in other words, keeps both. The web page
/// and MCP show the same.
/// </summary>
public sealed class OrderAssumptionContinuationTests
{
    private static OrderResult Base(params string[] clarifier) =>
        new(Guid.NewGuid(), OrderStateResolver.Resolve(OrderState.Ready, null), Guid.NewGuid(), 1, "Liebe Eltern", 2, [], [],
            [new OrderSourceUsageView("notizen.txt", SourceUsageMode.FullText, 400, null)], [], null,
            clarifier.Select((t, i) => new IntakeAssumption($"a{i + 1}", GapTopic.Tone, t, AssumptionConfidence.Sure)).ToList());

    private static RunReport Report(params string[] writer) =>
        new(2, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            [new RunReportSource(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400)], 1, 1, null, [], WriterNotesStatus.Ok,
            new WriterNotes(writer, [], []), [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.Checked, [], [], [], 1, "m", false, "fact-check-1"),
            null, []);

    private static OrderResult Shown(string[] clarifier, params string[] writer) => Base(clarifier).WithReport(Report(writer));

    [Fact]
    public void A_writer_assumption_the_clarifier_continues_after_a_comma_is_shown_once_as_the_longer_one()
    {
        var result = Shown(["Eltern werden mit Sie angesprochen, auch im Betreff."], "Die Eltern werden mit Sie angesprochen.");

        Assert.Equal(["Eltern werden mit Sie angesprochen, auch im Betreff."], result.AllAssumptions());
        Assert.Empty(result.WriterAssumptions!);
    }

    [Fact]
    public void An_assumption_of_the_clarifier_is_never_left_out_also_when_the_writer_continues_it()
    {
        var result = Shown(["Eltern werden mit Sie angesprochen."], "Eltern werden mit Sie angesprochen, Kinder mit du.");

        Assert.Equal(["Eltern werden mit Sie angesprochen.", "Eltern werden mit Sie angesprochen, Kinder mit du."], result.AllAssumptions());
    }

    [Fact]
    public void Assumptions_of_the_clarifier_that_continue_each_other_both_stay()
    {
        var result = Base("Der Ton ist freundlich.", "Der Ton ist freundlich, aber sachlich.");

        Assert.Equal(["Der Ton ist freundlich.", "Der Ton ist freundlich, aber sachlich."], result.AllAssumptions());
    }

    [Fact]
    public void The_longer_writer_assumption_is_shown_where_the_first_stood()
    {
        var result = Shown(["Freundlicher Ton."],
            "Der Elternabend ist im Musiksaal.",
            "Die Anmeldung erfolgt per Rückmeldezettel.",
            "Wir bitten um Rückmeldung.",
            "Die Anmeldung erfolgt per Rückmeldezettel (bis Freitag).");

        Assert.Equal(
            ["Freundlicher Ton.", "Der Elternabend ist im Musiksaal.", "Die Anmeldung erfolgt per Rückmeldezettel (bis Freitag).", "Wir bitten um Rückmeldung."],
            result.AllAssumptions());
        Assert.Equal(
            ["Der Elternabend ist im Musiksaal.", "Die Anmeldung erfolgt per Rückmeldezettel (bis Freitag).", "Wir bitten um Rückmeldung."],
            result.WriterAssumptions);
    }

    [Fact]
    public void A_chain_of_continuations_shows_only_the_longest_where_the_first_stood()
    {
        var result = Shown([], "Die Eltern werden gesiezt.", "Der Brief ist kurz.", "Eltern werden gesiezt, auch im Betreff.",
            "Eltern werden gesiezt, auch im Betreff; Kinder werden geduzt.");

        Assert.Equal(["Eltern werden gesiezt, auch im Betreff; Kinder werden geduzt.", "Der Brief ist kurz."], result.AllAssumptions());
    }

    [Theory]
    [InlineData("Eltern werden mit Sie angesprochen.", "Eltern werden mit Sie angesprochen, auch im Betreff.", true)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Eltern werden mit Sie angesprochen; Kinder mit du.", true)]
    [InlineData("Anrede", "Anrede: Liebe Eltern", true)]
    [InlineData("Der Termin ist am Freitag.", "Der Termin ist am Freitag (19 Uhr).", true)]
    [InlineData("Der Termin ist am Freitag.", "Der Termin ist am Freitag – im Musiksaal.", true)]
    [InlineData("Der Termin ist am Freitag.", "Der Termin ist am Freitag — im Musiksaal.", true)]
    [InlineData("Der Termin ist am Freitag.", "Der Termin ist am Freitag - im Musiksaal.", true)]
    [InlineData("The parents are addressed formally.", "Parents are addressed formally, also in the subject line.", true)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Eltern werden mit Sie angesprochen und geduzt.", false)]
    [InlineData("Eltern werden mit Sie angesprochen.", "Eltern werden nicht mit Sie angesprochen.", false)]
    [InlineData("Eltern werden angesprochen.", "Eltern werden angesprochen nicht aber die Kinder.", false)]
    [InlineData("Eltern werden mit Sie", "Eltern werden mit Sie-Form angesprochen.", false)]
    [InlineData("Der Beitrag ist 3", "Der Beitrag ist 35 Euro, zahlbar bar.", false)]
    [InlineData("Der Ton ist freundlich.", "Freundlicher Ton, aber sachlich.", false)]
    [InlineData("Der Ton ist freundlich.", "Der Ton ist freundlich.", false)]
    [InlineData("", "Der Ton ist freundlich, aber sachlich.", false)]
    [InlineData("eltern werden geduzt", "Eltern werden geduzt, außer im Betreff", true)]
    [InlineData("Eltern werden", "Eltern werden nicht geduzt, sondern gesiezt", false)]
    [InlineData("Eltern werden geduzt", "Eltern werden geduztx, ja", false)]
    [InlineData("Das Treffen beginnt um 18", "Das Treffen beginnt um 18:30 Uhr.", false)]
    [InlineData("Das Budget beträgt 5", "Das Budget beträgt 5,5 Mio. Euro.", false)]
    [InlineData("Die Gebühr beträgt 12.", "Die Gebühr beträgt 12,50 Euro.", false)]
    [InlineData("Beginn ist um 19", "Beginn ist um 19:30", false)]
    [InlineData("Mitbringen: Stifte", "Mitbringen: Stifte;Papier", false)]
    [InlineData("Mitbringen: Stifte", "Mitbringen: Stifte; Papier", true)]
    public void Only_a_word_for_word_beginning_followed_by_a_punctuation_mark_continues(string shorter, string longer, bool continued)
    {
        Assert.Equal(continued, OrderResultRules.ContinuedBy(shorter, longer));
        Assert.False(OrderResultRules.ContinuedBy(longer, shorter));

        var shown = Shown([], shorter, longer).AllAssumptions();

        if (string.IsNullOrWhiteSpace(shorter))
            Assert.Equal([longer], shown);
        else if (continued)
            Assert.Equal([longer], shown);
        else if (OrderResultRules.SameAssumptionKey(shorter).Equals(OrderResultRules.SameAssumptionKey(longer), StringComparison.OrdinalIgnoreCase))
            Assert.Equal([shorter], shown);
        else
            Assert.Equal([shorter, longer], shown);
    }

    [Fact]
    public void Assumptions_that_say_the_same_in_other_words_are_both_shown_because_only_a_person_can_tell()
    {
        var result = Shown(["Freundlich, herzlich, aber klar und sachlich."], "Der Ton ist freundlich und herzlich, aber sachlich gehalten.",
            "Die Eltern werden direkt angesprochen.", "Eltern werden persönlich angesprochen, mit Sie.");

        Assert.Equal(
            ["Freundlich, herzlich, aber klar und sachlich.", "Der Ton ist freundlich und herzlich, aber sachlich gehalten.",
                "Die Eltern werden direkt angesprochen.", "Eltern werden persönlich angesprochen, mit Sie."],
            result.AllAssumptions());
    }

    [Fact]
    public void The_web_page_and_mcp_show_the_same_assumptions()
    {
        var result = Shown(["Eltern werden mit Sie angesprochen, auch im Betreff.", "Der Ton ist freundlich."],
            "Eltern werden mit Sie angesprochen.", "Der Ton ist freundlich, aber sachlich.", "Die Rückmeldung erfolgt bis Freitag.",
            "Die Rückmeldung erfolgt bis Freitag: per Zettel.");

        var dto = result.ToDto();
        var mcp = dto.Assumptions!.Select(a => a.Text).Concat(dto.WriterAssumptions!).ToList();

        Assert.Equal(result.AllAssumptions(), mcp);
        Assert.Equal(
            ["Eltern werden mit Sie angesprochen, auch im Betreff.", "Der Ton ist freundlich.", "Der Ton ist freundlich, aber sachlich.",
                "Die Rückmeldung erfolgt bis Freitag: per Zettel."],
            mcp);
    }

    [Fact]
    public void Two_different_continuations_of_one_assumption_are_both_shown_the_longest_where_the_shorter_stood()
    {
        var result = Shown(["Freundlicher Ton."], "Eltern werden gesiezt.", "Der Brief ist kurz.", "Eltern werden gesiezt – außer im Betreff.",
            "Eltern werden gesiezt, auch in der Anrede und im Gruß.");

        Assert.Equal(
            ["Freundlicher Ton.", "Eltern werden gesiezt, auch in der Anrede und im Gruß.", "Der Brief ist kurz.", "Eltern werden gesiezt – außer im Betreff."],
            result.AllAssumptions());
    }

    [Fact]
    public void An_assumption_of_the_clarifier_is_never_left_out_also_when_a_writer_assumption_with_the_same_words_came_first()
    {
        var shown = OrderResultRules.WithoutRepeatedAssumptions(
            [("Der Ton ist sachlich.", false), ("Der Ton ist sachlich, auch im Gruß.", false), ("Der Ton ist sachlich.", true)]);

        Assert.Equal([("Der Ton ist sachlich.", true), ("Der Ton ist sachlich, auch im Gruß.", false)], shown);
        Assert.Equal([("Die Eltern werden gesiezt.", true)],
            OrderResultRules.WithoutRepeatedAssumptions([("Eltern werden gesiezt", false), ("Die Eltern werden gesiezt.", true)]));
    }

    [Fact]
    public void A_fact_left_open_never_hides_a_shorter_assumption_of_the_writer()
    {
        const string open = "Der Termin ist am Freitag, um 19 Uhr.";
        var result = (Base(open) with { NotGivenFacts = [open] }).WithReport(Report("Der Termin ist am Freitag."));

        Assert.Equal(["Der Termin ist am Freitag."], result.WriterAssumptions);
        Assert.Equal(["Der Termin ist am Freitag."], result.AllAssumptions());
        Assert.Equal([open], result.NotGivenFacts());
        Assert.Equal(["Der Termin ist am Freitag."], result.ToDto().WriterAssumptions);
    }

    [Fact]
    public void A_fact_left_open_with_the_same_words_as_a_writer_assumption_is_shown_once_under_left_open_as_before()
    {
        const string open = "Der Termin ist am Freitag.";
        var result = (Base(open) with { NotGivenFacts = [open] }).WithReport(Report("Der Termin ist am Freitag.", "Der Ton ist freundlich."));

        Assert.Equal(["Der Ton ist freundlich."], result.WriterAssumptions);
        Assert.Equal(["Der Ton ist freundlich."], result.AllAssumptions());
        Assert.Equal([open], result.NotGivenFacts());
    }
}
