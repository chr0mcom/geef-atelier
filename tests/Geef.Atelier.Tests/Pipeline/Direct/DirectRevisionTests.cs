using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// A run that changes an earlier version on the direct path, piece by piece: the writer's instruction (only with the change), the length
/// of the first request as a note, and the report that keeps the open findings and open points of the earlier version (contract stage 5,
/// 4.2 "never lose across versions").
/// </summary>
public sealed class DirectRevisionTests
{
    private const string Brief = "Schreib eine Einladung zum Elternabend, max. 60 Wörter.";

    private const string Text = "Liebe Eltern, der Elternabend ist am 5. Mai um 19 Uhr in Raum 12. Bitte melden Sie sich im Sekretariat an. Wir freuen uns auf Sie.";

    private static readonly LengthConstraint AtMostTen = new(LengthUnit.Words, null, 10, "max. 10 Wörter", LengthKind.AtMost);

    private static readonly FactFinding Office = new("f1", FactFindingKind.Unsupported, "Bitte melden Sie sich im Sekretariat an.",
        "Nobody said where to sign up.", null, null, null, null, null, true, QuoteMatchKind.Exact);

    private static readonly FactFinding Gone = new("f2", FactFindingKind.Unsupported, "Es gibt Kuchen.",
        "No cake in the material.", null, null, null, null, null, true, QuoteMatchKind.Exact);

    private static readonly FactFinding Conflict = new("f3", FactFindingKind.SourceConflict, null, "Two rooms.", null,
        "Raum 12", "Raum 14", "notizen.txt", null, false, QuoteMatchKind.NotFound);

    private static FactCheckSummary Checked(FactCheckState state, params FactFinding[] open) =>
        new(FactCheckMode.On, FactCheckScope.Full, state, open.Where(f => f.Kind != FactFindingKind.SourceConflict).ToList(),
            open.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList(), [], 1, "gpt-astra-latest", false, FactCheckPrompts.Version);

    private static RunReport Earlier(FactCheckSummary? factCheck = null, IReadOnlyList<string>? openPoints = null, IReadOnlyList<RunReportEarlierPoint>? earlierPoints = null) =>
        DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, new WriterNotes([], openPoints ?? [], []), WriterNotesStatus.Ok))) with
        {
            FactCheck = factCheck,
            EarlierOpenPoints = earlierPoints,
        };

    private static FactCheckRound Round(params FactFinding[] open) =>
        new(1, FactCheckMode.On, FactCheckScope.Full, FactRoundStatus.Checked, open, 1, "gpt-astra-latest", false);

    private static RunReport Build(string text, DirectRunFacts facts, OrderConstraints? constraints = null) =>
        DirectReportBuilder.Build(constraints ?? OrderConstraints.None, text, [], 1, 1, null, facts);

    [Fact]
    public void The_writer_gets_the_revision_instruction_instead_of_the_seed_instruction_only_for_a_change()
    {
        var plain = DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: true, seed: Text);
        var change = DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: true, seed: Text, revision: true);

        Assert.Contains(DirectWriterPrompts.SeedInstruction, plain, StringComparison.Ordinal);
        Assert.DoesNotContain(DirectWriterPrompts.RevisionInstruction, plain, StringComparison.Ordinal);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, change, StringComparison.Ordinal);
        Assert.DoesNotContain(DirectWriterPrompts.SeedInstruction, change, StringComparison.Ordinal);
        Assert.Equal(plain.Replace(DirectWriterPrompts.SeedInstruction, DirectWriterPrompts.RevisionInstruction, StringComparison.Ordinal), change);
        Assert.Equal(
            "Change the text in draft.md only as the person asks under \"Change requested by the person\". Keep everything else as it is: the same facts, structure and wording. Keep the language of the text. Keep the length within ±15 % unless the change asks for another length.",
            DirectWriterPrompts.RevisionInstruction);
    }

    [Fact]
    public void An_api_provider_gets_the_old_text_in_the_prompt_and_the_instruction_about_this_text()
    {
        var prompt = DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: false, seed: Text, revision: true);

        Assert.Contains("<<<TEXT\n" + Text + "\nTEXT>>>", prompt, StringComparison.Ordinal);
        Assert.Contains("Change this text only as the person asks under \"Change requested by the person\".", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("draft.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_seed_the_revision_flag_changes_nothing()
    {
        Assert.Equal(
            DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: true, seed: null),
            DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: true, seed: null, revision: true));
    }

    private static Task<ReviewResult> Review(string draft, OrderConstraints constraints, string? revisionHint)
    {
        IRunContext context = new RunContext().Set(AtelierContextKeys.CurrentDraft, draft).Set(AtelierContextKeys.OrderConstraints, constraints);
        if (revisionHint is not null)
            context = context.Set(AtelierContextKeys.RevisionHint, revisionHint);
        return new DeterministicChecksReviewer().ReviewAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task In_a_change_the_length_of_the_first_request_is_a_note_and_never_blocks()
    {
        var constraints = OrderConstraints.None with { Length = AtMostTen };

        var first = await Review(Text, constraints, revisionHint: null);
        var change = await Review(Text, constraints, revisionHint: "- (version 2) Make the text about a third longer without adding new facts.");

        Assert.Equal(ReviewDecision.Rejected, first.Decision);
        Assert.Equal(ReviewDecision.Approved, change.Decision);
        var note = Assert.Single(change.Findings);
        Assert.Equal(FindingSeverity.Info, note.Severity);
        Assert.Equal("length", note.Metadata!["code"]);
        Assert.Equal(false, note.Metadata["blocking"]);
        Assert.Equal(
            $"The text has {DraftCheckEvaluator.CountWords(Text)} words; your first request asked for \"max. 10 Wörter\". Because you asked for a change, the length was not corrected.",
            note.Message);
    }

    [Fact]
    public async Task In_a_change_every_other_check_still_blocks()
    {
        var constraints = OrderConstraints.None with
        {
            Length = AtMostTen,
            ForbidPlaceholders = true,
            ForbidPlaceholdersQuote = "ohne Platzhalter",
            Language = "de",
        };

        var result = await Review("Liebe Eltern, der Elternabend ist am [Datum] in der Aula der Schule, und wir freuen uns sehr auf Sie alle.", constraints, "- (version 2) Simpler words.");

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        var error = Assert.Single(result.Findings, f => f.Severity == FindingSeverity.Error);
        Assert.Equal(DraftCheckCodes.Placeholders, error.Metadata!["code"]);
        Assert.Contains(result.Findings, f => f.Severity == FindingSeverity.Info && (string)f.Metadata!["code"] == DraftCheckCodes.Length);
    }

    [Fact]
    public void A_passed_length_stays_as_it_is_in_a_change()
    {
        var constraints = OrderConstraints.None with { Length = AtMostTen with { Max = 100 } };

        Assert.Equal(DraftCheckEvaluator.Evaluate(Text, constraints), DeterministicChecksReviewer.Evaluate(Text, constraints, revision: true));
    }

    [Fact]
    public void The_report_of_a_change_shows_the_length_as_a_note()
    {
        var constraints = OrderConstraints.None with { Length = AtMostTen };

        var plain = Build(Text, DirectRunFacts.None, constraints);
        var change = Build(Text, new DirectRunFacts(Revision: new DirectRevision(1, null)), constraints);

        Assert.True(Assert.Single(plain.Checks, c => c.Code == DraftCheckCodes.Length).Blocking);
        var length = Assert.Single(change.Checks, c => c.Code == DraftCheckCodes.Length);
        Assert.False(length.Passed);
        Assert.False(length.Blocking);
        Assert.Contains("Because you asked for a change", length.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_findings_of_the_earlier_version_still_in_the_text_are_kept_with_their_version()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office, Gone, Conflict));
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        var passage = Assert.Single(report.FactCheck!.Open);
        Assert.Equal((Office.TextQuote, 1, true), (passage.TextQuote, passage.FromVersion, passage.Blocking));
        var conflict = Assert.Single(report.FactCheck.SourceConflicts);
        Assert.Equal((Conflict.MaterialQuote, 1), (conflict.MaterialQuote, conflict.FromVersion));
        Assert.Equal(FactCheckState.OpenPoints, report.FactCheck.State);
    }

    [Fact]
    public void A_finding_the_new_check_reports_again_is_shown_once_as_its_own()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office));
        var fresh = Office with { WhatIsWrong = "Still no place to sign up." };
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round(fresh)], Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        var passage = Assert.Single(report.FactCheck!.Open);
        Assert.Null(passage.FromVersion);
        Assert.Equal("Still no place to sign up.", passage.WhatIsWrong);
    }

    [Fact]
    public void A_finding_carried_over_twice_keeps_the_version_that_found_it()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office with { FromVersion = 1 }));
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(2, earlier));

        var report = Build(Text, facts);

        Assert.Equal(1, Assert.Single(report.FactCheck!.Open).FromVersion);
    }

    [Theory]
    [InlineData(FactCheckState.Off)]
    [InlineData(FactCheckState.NotShown)]
    public void Findings_of_an_earlier_check_that_was_off_or_not_shown_are_not_carried_over(FactCheckState state)
    {
        var earlier = Earlier(Checked(state, Office));
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        Assert.Empty(report.FactCheck!.Open);
        Assert.Equal(FactCheckState.Checked, report.FactCheck.State);
    }

    [Fact]
    public void Findings_of_the_earlier_version_stay_when_the_new_check_did_not_complete()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office));
        var failed = new FactCheckRound(1, FactCheckMode.On, FactCheckScope.Full, FactRoundStatus.Failed, [], 1, null, false);
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [failed], Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        Assert.Equal(FactCheckState.NotCompleted, report.FactCheck!.State);
        Assert.Equal(1, Assert.Single(report.FactCheck.Open).FromVersion);
    }

    [Fact]
    public void Open_points_of_the_writer_not_repeated_are_listed_with_their_version()
    {
        var earlier = Earlier(openPoints: ["Die Uhrzeit fehlte.", "Der Raum ist offen.", " "],
            earlierPoints: [new RunReportEarlierPoint(1, "Der Name der Lehrerin fehlt.")]);
        var notes = new WriterNotesSplit(Text, new WriterNotes([], ["der Raum ist offen"], []), WriterNotesStatus.Ok);

        var report = Build(Text, new DirectRunFacts(notes, Revision: new DirectRevision(2, earlier)));

        Assert.Equal([new RunReportEarlierPoint(1, "Der Name der Lehrerin fehlt."), new RunReportEarlierPoint(2, "Die Uhrzeit fehlte.")], report.EarlierOpenPoints);
        Assert.Equal(["der Raum ist offen"], report.WriterNotes!.OpenPoints);
    }

    [Fact]
    public void A_point_noted_twice_appears_once_with_the_version_that_noted_it_first()
    {
        var earlier = Earlier(openPoints: ["Die Uhrzeit fehlt."], earlierPoints: [new RunReportEarlierPoint(1, "Die Uhrzeit fehlt")]);

        var report = Build(Text, new DirectRunFacts(Revision: new DirectRevision(2, earlier)));

        Assert.Equal([new RunReportEarlierPoint(1, "Die Uhrzeit fehlt")], report.EarlierOpenPoints);
    }

    [Fact]
    public void Every_other_report_stays_as_it_was()
    {
        var plain = Build(Text, DirectRunFacts.None);
        var nothingEarlier = Build(Text, new DirectRunFacts(Revision: new DirectRevision(1, Earlier())));

        Assert.Null(plain.EarlierOpenPoints);
        Assert.Null(nothingEarlier.EarlierOpenPoints);
        Assert.DoesNotContain("earlierOpenPoints", RunReportJson.Serialize(plain), StringComparison.Ordinal);
        Assert.DoesNotContain("fromVersion", RunReportJson.Serialize(nothingEarlier), StringComparison.Ordinal);
    }

    [Fact]
    public void An_earlier_report_that_cannot_be_read_leaves_a_note()
    {
        var unreadable = Build(Text, new DirectRunFacts(Revision: new DirectRevision(1, null, ReportUnreadable: true)));
        var none = Build(Text, new DirectRunFacts(Revision: new DirectRevision(1, null)));

        Assert.Contains(DirectReportBuilder.EarlierReportMissingNote, unreadable.Notes!);
        Assert.DoesNotContain(DirectReportBuilder.EarlierReportMissingNote, none.Notes!);
    }

    [Fact]
    public void Carried_findings_and_earlier_points_survive_the_stored_json()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office), ["Die Uhrzeit fehlte."]);
        var report = Build(Text, new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(1, earlier)));

        var parsed = RunReportJson.TryParse(RunReportJson.Serialize(report))!;

        Assert.Equal(1, Assert.Single(parsed.FactCheck!.Open).FromVersion);
        Assert.Equal([new RunReportEarlierPoint(1, "Die Uhrzeit fehlte.")], parsed.EarlierOpenPoints);
    }
}
