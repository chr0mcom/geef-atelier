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
/// The result of a changed version shows what the report carried over from the version before: flagged passages keep the version
/// they came from, the open points of the writer for that version are listed, and "What was checked" never says there is nothing to
/// look at while such points are shown. Reports without these fields (schema 1, reports written before stage 5) stay as they were.
/// The About me rule applies to carried-over passages as to new ones.
/// </summary>
public sealed class OrderResultEarlierVersionTests
{
    private const string Text = "Liebe Eltern, das Treffen ist am Freitag um 18 Uhr in der Aula.";

    private static OrderResult Base() =>
        new(Guid.NewGuid(), OrderStateResolver.Resolve(OrderState.Ready, null), Guid.NewGuid(), 2, Text, 12, [], [],
            [new OrderSourceUsageView("notizen.txt", SourceUsageMode.FullText, 400, null)], [], null, []);

    private static FactFinding Unsupported(string quote, int? fromVersion = null) =>
        new("f1", FactFindingKind.Unsupported, quote, "Not in the material.", null, null, null, null, null, true, QuoteMatchKind.Exact,
            FromVersion: fromVersion);

    private static FactFinding AgainstProfile(int? fromVersion) =>
        new("f2", FactFindingKind.Instruction, "um 18 Uhr", "Goes against the instruction.", "Termine immer mit Wochentag nennen.", null, null,
            null, InstructionOrigin.Memory, true, QuoteMatchKind.Exact, FromVersion: fromVersion);

    private static RunReport Report(
        IReadOnlyList<FactFinding>? open = null,
        IReadOnlyList<string>? openPoints = null,
        IReadOnlyList<RunReportEarlierPoint>? earlier = null,
        FactCheckState? state = null,
        int schema = 2) =>
        new(schema, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            [new RunReportSource(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400)], 1, 1, null, [], WriterNotesStatus.Ok,
            new WriterNotes([], openPoints ?? [], []), [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full,
                state ?? ((open?.Count ?? 0) > 0 ? FactCheckState.OpenPoints : FactCheckState.Checked),
                open ?? [], [], [], 1, "m", false, "fact-check-1"),
            null, [], EarlierOpenPoints: earlier);

    [Fact]
    public void A_passage_against_an_instruction_from_before_the_latest_change_is_shown_as_a_hint_with_its_mark_and_the_about_me_rule()
    {
        var hint = AgainstProfile(fromVersion: null) with { Blocking = false, BeforeLastChange = true };
        var stillMarkedBlocking = AgainstProfile(fromVersion: 1) with { BeforeLastChange = true, TextQuote = "in der Aula" };

        var owner = Base().WithReport(Report(open: [hint, stillMarkedBlocking]));
        var other = Base().WithReport(Report(open: [hint]), showProfile: false);

        Assert.All(owner.UnbackedPassages!, p => Assert.Equal((false, true), (p.Blocking, p.BeforeLastChange)));
        Assert.Equal("Termine immer mit Wochentag nennen.", owner.UnbackedPassages![0].Instruction);
        Assert.Null(Assert.Single(other.UnbackedPassages!).Instruction);
        Assert.Empty(owner.BlockingPassages(FactFindingKind.Instruction));
        Assert.Equal(2, owner.HintPassages().Count);
    }

    [Fact]
    public void A_carried_over_passage_keeps_its_version_and_the_earlier_open_points_are_shown_without_blank_ones()
    {
        var report = Report(
            open: [Unsupported("in der Aula", fromVersion: 1), Unsupported("am Freitag")],
            openPoints: ["Wer bringt die Getränke mit?"],
            earlier: [new RunReportEarlierPoint(1, "  Es fehlt, bis wann man sich anmelden soll.  "), new RunReportEarlierPoint(1, " "), new RunReportEarlierPoint(2, "Der Raum ist offen.")]);

        var result = Base().WithReport(report);

        Assert.Equal([("in der Aula", (int?)1), ("am Freitag", null)], result.UnbackedPassages!.Select(p => (p.Quote, p.FromVersion)));
        Assert.Equal([new OrderEarlierPoint(1, "Es fehlt, bis wann man sich anmelden soll."), new OrderEarlierPoint(2, "Der Raum ist offen.")],
            result.EarlierOpenPoints);
        Assert.Equal(["Wer bringt die Getränke mit?"], result.WriterOpenPoints);
        Assert.True(result.HasOpenPoints());
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
    }

    [Fact]
    public void Earlier_open_points_alone_never_let_the_result_say_there_is_nothing_to_look_at()
    {
        var without = Base().WithReport(Report());
        Assert.False(without.HasOpenPoints());
        Assert.Equal(OrderCheckLines.ToReviewNothing, without.Checks!.ToReview);
        Assert.Null(without.EarlierOpenPoints);

        var with = Base().WithReport(Report(earlier: [new RunReportEarlierPoint(1, "Es fehlt, bis wann man sich anmelden soll.")]));

        Assert.Empty(with.WriterOpenPoints!);
        Assert.Empty(with.ShownPassages());
        Assert.True(with.HasOpenPoints());
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, with.Checks!.ToReview);
    }

    [Fact]
    public void Only_blank_earlier_points_count_as_none()
    {
        var result = Base().WithReport(Report(earlier: [new RunReportEarlierPoint(1, "  "), new RunReportEarlierPoint(1, "")]));

        Assert.Empty(result.EarlierOpenPoints!);
        Assert.Empty(result.ShownEarlierPoints());
        Assert.False(result.HasOpenPoints());
    }

    [Fact]
    public void The_rule_counts_earlier_points_also_on_a_result_built_without_a_report()
    {
        var result = Base() with { EarlierOpenPoints = [new OrderEarlierPoint(1, "Es fehlt die Uhrzeit.")] };

        Assert.True(result.HasOpenPoints());
        Assert.Equal([new OrderEarlierPoint(1, "Es fehlt die Uhrzeit.")], result.ShownEarlierPoints());
    }

    [Fact]
    public void A_schema_1_report_leaves_both_fields_null()
    {
        var report = Report(open: [Unsupported("in der Aula", fromVersion: 1)], earlier: [new RunReportEarlierPoint(1, "Offen.")], schema: 1);

        var result = Base().WithReport(report);

        Assert.Null(result.EarlierOpenPoints);
        Assert.Null(result.UnbackedPassages);
        Assert.False(result.HasOpenPoints());
    }

    [Fact]
    public void A_stored_report_written_before_the_fields_existed_reads_with_both_fields_null()
    {
        var json = RunReportJson.Serialize(Report(open: [Unsupported("in der Aula")], openPoints: ["Wer bringt die Getränke mit?"]));
        Assert.DoesNotContain("earlier", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fromVersion", json, StringComparison.OrdinalIgnoreCase);

        var result = Base().WithReport(RunReportJson.TryParse(json)!);

        Assert.Null(result.EarlierOpenPoints);
        Assert.Null(Assert.Single(result.UnbackedPassages!).FromVersion);
        Assert.Equal(["Wer bringt die Getränke mit?"], result.WriterOpenPoints);
    }

    [Fact]
    public void A_stored_report_with_the_fields_reads_back_into_the_result()
    {
        var json = RunReportJson.Serialize(Report(open: [Unsupported("in der Aula", fromVersion: 1)], earlier: [new RunReportEarlierPoint(1, "Offen.")]));

        var result = Base().WithReport(RunReportJson.TryParse(json)!);

        Assert.Equal(1, Assert.Single(result.UnbackedPassages!).FromVersion);
        Assert.Equal([new OrderEarlierPoint(1, "Offen.")], result.EarlierOpenPoints);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void A_passage_against_the_about_me_details_shows_the_instruction_only_to_the_owner_also_when_carried_over(int? fromVersion)
    {
        var report = Report(open: [AgainstProfile(fromVersion)]);

        var owner = Assert.Single(Base().WithReport(report, showProfile: true).UnbackedPassages!);
        var other = Assert.Single(Base().WithReport(report, showProfile: false).UnbackedPassages!);

        Assert.Equal("Termine immer mit Wochentag nennen.", owner.Instruction);
        Assert.Null(other.Instruction);
        Assert.Equal(fromVersion, owner.FromVersion);
        Assert.Equal(fromVersion, other.FromVersion);
        Assert.Equal(owner.Quote, other.Quote);
    }

    [Theory]
    [InlineData(FactCheckState.NotShown)]
    [InlineData(FactCheckState.Off)]
    public void With_the_check_off_or_in_shadow_mode_carried_over_passages_stay_visible_and_its_own_stay_hidden(FactCheckState state)
    {
        var conflict = new FactFinding("f3", FactFindingKind.SourceConflict, null, "Two rooms.", null, "Raum 12", "Raum 14", "notizen.txt", null, false,
            QuoteMatchKind.NotFound, MaterialPart.Files, MaterialPart.Files, "notizen.txt", FromVersion: 1);
        var report = Report(open: [Unsupported("in der Aula", fromVersion: 1), Unsupported("am Freitag")], earlier: [new RunReportEarlierPoint(1, "Offen.")],
            state: state) is var r ? r with { FactCheck = r.FactCheck! with { SourceConflicts = [conflict] } } : null!;

        var result = Base().WithReport(report);

        var passage = Assert.Single(result.UnbackedPassages!);
        Assert.Equal(("in der Aula", (int?)1), (passage.Quote, passage.FromVersion));
        Assert.Single(result.SourceIssues!);
        Assert.Equal([new OrderEarlierPoint(1, "Offen.")], result.EarlierOpenPoints);
        Assert.True(result.HasOpenPoints());
        Assert.Contains(OrderCheckLines.FactsNotChecked + OrderCheckLines.FactsEarlierOne, result.Checks!.Checked);
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks.ToReview);
    }

    [Fact]
    public void Assumptions_of_the_writer_for_an_earlier_version_keep_their_kind_and_one_the_clarifier_shows_is_not_repeated()
    {
        var report = Report(earlier:
        [
            new RunReportEarlierPoint(1, "Die Anmeldefrist ist Freitag.", EarlierPointKind.Assumption),
            new RunReportEarlierPoint(1, "Die Eltern werden mit Sie angesprochen.", EarlierPointKind.Assumption),
            new RunReportEarlierPoint(1, "1 more note of the writer could not be shown because it holds a link, an address, or account or access details.", EarlierPointKind.Note),
        ]);

        var result = (Base() with { Assumptions = [new IntakeAssumption("a1", GapTopic.Tone, "Eltern werden mit Sie angesprochen", AssumptionConfidence.Sure)] })
            .WithReport(report);

        Assert.Equal(
            [new OrderEarlierPoint(1, "Die Anmeldefrist ist Freitag.", EarlierPointKind.Assumption),
             new OrderEarlierPoint(1, "1 more note of the writer could not be shown because it holds a link, an address, or account or access details.", EarlierPointKind.Note)],
            result.EarlierOpenPoints);
        Assert.True(result.HasOpenPoints());
    }
}
