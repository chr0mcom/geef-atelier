using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// "What was checked" for a version that changed an earlier one (review E5B F2/F3): a rule read from the first request that the new
/// version keeps is named as a rule of the first request, never as if the change had been checked ("has the requested length"); one it
/// does not keep is named as not kept, with the request to check whether the change meant it. The checks that always apply keep their
/// usual sentences.
/// </summary>
public sealed class OrderCheckLinesRevisionTests
{
    private static readonly RunReportSource File = new(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400);

    private static DraftCheckResult Check(string code, bool passed = true, bool blocking = true, string? quote = null) => new(code, passed, blocking, "ok", quote);

    private static RunReport Report(IReadOnlyList<DraftCheckResult> checks, bool revision) =>
        new(RunReport.CurrentSchemaVersion, RunReport.DirectKind, OrderConstraints.None, checks, [], [File], 1, 1, null, [],
            WriterNotesStatus.Off, WriterNotes.Empty, [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.Checked, [], [], [], 1, "m", false, "fact-check-1"),
            null, [], Revision: revision);

    private static readonly DraftCheckResult[] Kept =
    [
        Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble), Check(DraftCheckCodes.Length, quote: "höchstens 200 Wörter"),
        Check(DraftCheckCodes.Language), Check(DraftCheckCodes.Placeholders, quote: "ohne Platzhalter"), Check(DraftCheckCodes.Lists, quote: "keine Aufzählungen"),
        Check(DraftCheckCodes.ForbiddenTerm, quote: "nicht \"Aula\""), Check(DraftCheckCodes.RequiredTerm, quote: "mit \"Anmeldung\""),
    ];

    [Fact]
    public void Rules_of_the_first_request_a_changed_version_keeps_are_named_as_such_and_never_as_the_requested_length()
    {
        var view = OrderCheckLines.Build(Report(Kept, revision: true), hasOpenPoints: false);

        Assert.Equal("Checked automatically: the text is not empty, starts directly.", view.Checked[0]);
        Assert.Equal(
            "Checked automatically against the rules of your first request (your change may have replaced them): the text has the length it asked for, "
            + "is in the language it asked for, has no placeholders, avoids lists and the words it ruled out, uses the words it asked for.",
            view.Checked[1]);
        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderCheckLines.PassedLength, StringComparison.Ordinal));
        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderCheckLines.PassedLanguage, StringComparison.Ordinal));
        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderCheckLines.PassedRequiredWords, StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_change_the_sentences_stay_as_they_were()
    {
        var view = OrderCheckLines.Build(Report(Kept, revision: false), hasOpenPoints: false);

        Assert.StartsWith("Checked automatically: the text is not empty, starts directly, has the requested length, is in the requested language, has no placeholders",
            view.Checked[0], StringComparison.Ordinal);
        Assert.DoesNotContain(view.Checked, l => l.StartsWith(OrderCheckLines.FirstRequestLead, StringComparison.Ordinal));
    }

    [Fact]
    public void Rules_of_the_first_request_a_changed_version_does_not_keep_are_named_as_not_kept_and_the_always_checks_stay_apart()
    {
        DraftCheckResult[] checks =
        [
            Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble, passed: false, blocking: false),
            Check(DraftCheckCodes.Length, passed: false, blocking: false, quote: "höchstens 200 Wörter"),
            Check(DraftCheckCodes.Lists, passed: false, blocking: false, quote: "keine Aufzählungen"),
            Check(DraftCheckCodes.Placeholders, passed: true, blocking: false),
            Check(DraftCheckCodes.Arithmetic, passed: false),
        ];

        var view = OrderCheckLines.Build(Report(checks, revision: true), hasOpenPoints: true);

        Assert.Equal(
        [
            "Checked automatically: the text is not empty.",
            "Checked automatically and not met – see the open points below: the text may start with a remark to the reader, has a calculation that looks wrong.",
            "Rules of your first request the new version does not keep – please check whether your change meant this (see the open points below): the text does not have the length it asked for, uses lists.",
        ], view.Checked.Take(3));
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, view.ToReview);
    }
}
