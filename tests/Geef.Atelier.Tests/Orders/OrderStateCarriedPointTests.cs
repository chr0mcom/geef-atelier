using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// A new version that keeps a blocking passage of the version before open is shown as "Ready – please check", like a version that found
/// the passage itself: only while the fact check blocks (<see cref="FactCheckMode.On"/>), only for a change of an earlier version, and never
/// for stored reports without the field <c>fromVersion</c> or that cannot be read (older runs stay as they were).
/// </summary>
public sealed class OrderStateCarriedPointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private const string Text = "Liebe Eltern, der Elternabend ist am 5. Mai um 19 Uhr in der Aula. Wir freuen uns auf Sie.";

    private static FactFinding Passage(bool blocking = true, int? fromVersion = 1, FactFindingKind kind = FactFindingKind.Unsupported) =>
        new("f1", kind, "Wir freuen uns auf Sie.", "Nobody said that.", null, null, null, null, null, blocking, QuoteMatchKind.Exact, FromVersion: fromVersion);

    private static string Report(FactCheckMode mode, params FactFinding[] open) =>
        RunReportJson.Serialize(new RunReport(2, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            [new RunReportSource(null, "notes.md", SourceUsageMode.FullText, 100)], 1, 1, FactCheck:
            new FactCheckSummary(mode, FactCheckScope.Full, open.Length > 0 ? FactCheckState.OpenPoints : FactCheckState.Checked, open, [], [], 1, "m", false, "fact-check-1")));

    private static RunEntity Run(string? reportJson, string? revisionHint = "- (version 2) The meeting is on 5 May, not 12 May.", RunStatus status = RunStatus.Completed,
        RunOutcome? outcome = RunOutcome.Converged, string? text = Text) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = Now.AddMinutes(-10), Status = status, BriefingText = "b", ConfigJson = "{}", Kind = RunKind.Standard,
        Outcome = outcome, CompletedAt = status is RunStatus.Pending or RunStatus.Running ? null : Now.AddMinutes(-5), FinalText = text,
        OrderVersion = 2, RevisionHint = revisionHint, ReportJson = reportJson,
    };

    private static OrderDisplayState Shown(RunEntity run) => OrderStateResolver.Resolve(OrderState.Ready, run.ToRunState(Now)).State;

    [Fact]
    public void A_blocking_passage_of_the_version_before_makes_a_finished_change_ready_to_check()
    {
        var run = Run(Report(FactCheckMode.On, Passage()));

        Assert.True(run.ToRunState(Now).CarriesBlockingPoint);
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, Shown(run));
    }

    [Theory]
    [InlineData(FactFindingKind.Instruction)]
    [InlineData(FactFindingKind.Superseded)]
    public void Every_kind_of_blocking_passage_counts(FactFindingKind kind) =>
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, Shown(Run(Report(FactCheckMode.On, Passage(kind: kind)))));

    [Fact]
    public void A_carried_passage_among_own_findings_still_counts() =>
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints,
            Shown(Run(Report(FactCheckMode.On, Passage(blocking: false, fromVersion: null), Passage(fromVersion: 1)))));

    [Fact]
    public void A_run_whose_status_is_failed_but_converged_follows_the_same_rule() =>
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, Shown(Run(Report(FactCheckMode.On, Passage()), status: RunStatus.Failed)));

    [Fact]
    public void In_report_mode_a_carried_passage_shows_ready_as_an_own_one_would() =>
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(Report(FactCheckMode.Report, Passage()))));

    [Fact]
    public void A_passage_that_does_not_block_or_was_found_by_the_version_itself_changes_nothing()
    {
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(Report(FactCheckMode.On, Passage(blocking: false)))));
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(Report(FactCheckMode.On, Passage(fromVersion: null)))));
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(Report(FactCheckMode.On))));
    }

    [Fact]
    public void A_run_that_changes_no_earlier_version_never_reads_its_report_for_this()
    {
        var resumed = Run(Report(FactCheckMode.On, Passage()), revisionHint: null);

        Assert.False(resumed.ToRunState(Now).CarriesBlockingPoint);
        Assert.Equal(OrderDisplayState.Delivered, Shown(resumed));
    }

    [Fact]
    public void Older_stored_reports_without_the_version_field_or_unreadable_ones_stay_as_they_were()
    {
        const string olderOn = """{"schemaVersion":2,"kind":"direct","constraints":{},"checks":[],"placeholders":[],"sources":[],"deliveredIteration":1,"iterations":1,"factCheck":{"mode":"On","scope":"Full","state":"OpenPoints","open":[{"id":"f1","kind":"Unsupported","textQuote":"Wir freuen uns auf Sie.","whatIsWrong":"x","blocking":true,"match":"Exact"}],"sourceConflicts":[],"corrected":[],"calls":1,"usedFallbackModel":false,"promptVersion":"fact-check-1"}}""";
        const string withoutOpen = """{"schemaVersion":2,"kind":"direct","checks":[],"placeholders":[],"sources":[],"iterations":1,"factCheck":{"mode":"On","scope":"Full","state":"Checked","calls":1,"usedFallbackModel":false,"promptVersion":"fact-check-1"}}""";
        const string schemaOne = """{"schemaVersion":1,"kind":"direct","checks":[],"placeholders":[],"sources":[],"iterations":1}""";

        foreach (var json in new[] { olderOn, withoutOpen, schemaOne, "{not json", "", null })
            Assert.Equal(OrderDisplayState.Delivered, Shown(Run(json)));
    }

    [Theory]
    [InlineData(FactCheckMode.Off)]
    [InlineData(FactCheckMode.Shadow)]
    [InlineData(FactCheckMode.Report)]
    [InlineData(FactCheckMode.On)]
    public void A_change_of_a_version_without_report_that_needed_a_check_is_ready_to_check_in_every_mode(FactCheckMode mode)
    {
        var json = Report(mode).Replace("\"schemaVersion\":2,", "\"schemaVersion\":2,\"earlierNeedsCheck\":true,", StringComparison.Ordinal);

        Assert.True(Run(json).ToRunState(Now).CarriesBlockingPoint);
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, Shown(Run(json)));
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(json, revisionHint: null)));
        Assert.Equal(OrderDisplayState.Delivered, Shown(Run(Report(mode))));
    }

    [Theory]
    [InlineData(RunStatus.Running, null, OrderDisplayState.Working)]
    [InlineData(RunStatus.Pending, null, OrderDisplayState.Queued)]
    [InlineData(RunStatus.Aborted, RunOutcome.Aborted, OrderDisplayState.Stopped)]
    public void Only_a_version_shown_as_ready_is_changed(RunStatus status, RunOutcome? outcome, OrderDisplayState expected) =>
        Assert.Equal(expected, Shown(Run(Report(FactCheckMode.On, Passage()), status: status, outcome: outcome)));

    [Fact]
    public void A_version_without_text_and_a_stopped_order_are_not_changed()
    {
        var withoutText = Run(Report(FactCheckMode.On, Passage()), outcome: RunOutcome.Failed, text: null);
        Assert.False(withoutText.ToRunState(Now).CarriesBlockingPoint);
        Assert.Equal(OrderDisplayState.Failed, Shown(withoutText));

        var carrying = Run(Report(FactCheckMode.On, Passage())).ToRunState(Now);
        Assert.Equal(OrderDisplayState.Stopped, OrderStateResolver.Resolve(OrderState.Cancelled, carrying).State);
    }

    [Fact]
    public void Resolve_upgrades_only_the_ready_state()
    {
        foreach (var status in Enum.GetValues<RunStatus>())
        foreach (var outcome in Enum.GetValues<RunOutcome>().Select(o => (RunOutcome?)o).Append(null))
        foreach (var hasText in new[] { true, false })
        {
            var plain = OrderStateResolver.Resolve(OrderState.Ready, new OrderRunState(status, outcome, RunKind.Standard, hasText));
            var carrying = OrderStateResolver.Resolve(OrderState.Ready, new OrderRunState(status, outcome, RunKind.Standard, hasText, CarriesBlockingPoint: true));
            var expected = plain.State == OrderDisplayState.Delivered ? plain with { State = OrderDisplayState.DeliveredWithOpenPoints } : plain;
            Assert.Equal(expected, carrying);
        }
    }
}
