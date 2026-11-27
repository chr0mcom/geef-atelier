using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Sdk.Context;
using Geef.Sdk.Results;
using RunEntity = Geef.Atelier.Core.Domain.RunEntity;
using RunOutcome = Geef.Atelier.Core.Domain.RunOutcome;
using RunStatus = Geef.Atelier.Core.Domain.RunStatus;

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
            "Change the text in draft.md only as the person asks in the line marked \"the change to make now\" under \"Change requested by the person\". The earlier changes listed there are already made in the text: keep them and do not make them again. Keep everything else as it is: the same facts, structure and wording. If the change leaves another sentence wrong or pointing at something that is no longer in the text, adjust that sentence as little as possible. A change of tone, style or length changes the wording, not the content: add no statement about what happened, what will happen, what is offered or what is expected that the text does not already make, and keep who writes and who is addressed. When the text has to get longer, expand on what it already says and do not invent detail to fill it. Keep the language of the text unless the change asks for another language. Keep the length within ±15 % unless the change asks for another length or cannot be made without changing it.",
            DirectWriterPrompts.RevisionInstruction);
    }

    [Fact]
    public void An_api_provider_gets_the_old_text_in_the_prompt_and_the_instruction_about_this_text()
    {
        var prompt = DirectWriterPrompts.FirstPass(Brief, OrderSourceBundle.Empty, documentMode: false, seed: Text, revision: true);

        Assert.Contains("<<<TEXT\n" + Text + "\nTEXT>>>", prompt, StringComparison.Ordinal);
        Assert.Contains("Change this text only as the person asks in the line marked \"the change to make now\" under \"Change requested by the person\".", prompt, StringComparison.Ordinal);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction.Replace("the text in draft.md", "this text", StringComparison.Ordinal), prompt, StringComparison.Ordinal);
        Assert.Contains("keep who writes and who is addressed. When the text has to get longer, expand on what it already says", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("draft.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_seed_instruction_and_the_sentences_of_the_quick_buttons_stay_word_for_word()
    {
        Assert.Equal("Revise the text in draft.md as the request asks. Change as little as possible; keep length within ±15 % unless the request says otherwise.",
            DirectWriterPrompts.SeedInstruction);
        Assert.Equal(
            [
                "Make the text about a third shorter.",
                "Make the text about a third longer without adding new facts.",
                "Make the tone more formal.",
                "Make the tone warmer and friendlier.",
                "Use simpler words and shorter sentences.",
            ],
            Enum.GetValues<RevisionQuick>().Select(q => q.Instruction()));
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
            $"Your first request asked for \"max. 10 Wörter\"; the new version does not keep to it: it has {DraftCheckEvaluator.CountWords(Text)} words. Please check whether your change meant this.",
            note.Message);
    }

    [Fact]
    public async Task In_a_change_every_rule_of_the_first_request_is_a_note_and_the_checks_that_always_apply_still_block()
    {
        var constraints = OrderConstraints.None with
        {
            Length = AtMostTen,
            ForbidPlaceholders = true,
            ForbidPlaceholdersQuote = "ohne Platzhalter",
            Language = "en",
            ForbiddenPatterns = [new PatternRule(ForbiddenPattern.Lists, "keine Aufzählungen")],
            ForbiddenTerms = [new TermRule("Aula", "nicht \"Aula\"")],
            RequiredTerms = [new TermRule("Anmeldung", "mit \"Anmeldung\"")],
        };
        const string draft = "Liebe Eltern, der Elternabend ist am [Datum] in der Aula der Schule, und wir freuen uns sehr auf Sie alle.\n\n- Beginn 19 Uhr\n- Ende 21 Uhr\n\nKosten: 22 + 31 = 60 Euro.";

        var first = await Review(draft, constraints, revisionHint: null);
        var change = await Review(draft, constraints, "Set the times as a list.");

        Assert.Equal(
            [DraftCheckCodes.Length, DraftCheckCodes.Language, DraftCheckCodes.Placeholders, DraftCheckCodes.Lists, DraftCheckCodes.ForbiddenTerm, DraftCheckCodes.RequiredTerm, DraftCheckCodes.Arithmetic],
            first.Findings.Where(f => f.Severity == FindingSeverity.Error).Select(f => (string)f.Metadata!["code"]).ToArray());
        var error = Assert.Single(change.Findings, f => f.Severity == FindingSeverity.Error);
        Assert.Equal(DraftCheckCodes.Arithmetic, error.Metadata!["code"]);
        var notes = change.Findings.Where(f => f.Severity == FindingSeverity.Info && (string)f.Metadata!["code"] != DraftCheckCodes.Preamble).ToList();
        Assert.Equal(6, notes.Count);
        Assert.All(notes, n => Assert.Equal(false, n.Metadata!["blocking"]));
        Assert.All(notes, n => Assert.StartsWith("Your first request asked for ", n.Message, StringComparison.Ordinal));
        Assert.All(notes, n => Assert.EndsWith("Please check whether your change meant this.", n.Message, StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Message == "Your first request asked for \"keine Aufzählungen\"; the new version does not keep to it: it uses lists. Please check whether your change meant this.");
        Assert.Contains(notes, n => n.Message == "Your first request asked for a text in English; the new version does not keep to it: it is in German. Please check whether your change meant this.");
        Assert.Contains(notes, n => n.Message == "Your first request asked for \"nicht \"Aula\"\"; the new version does not keep to it: it uses \"Aula\". Please check whether your change meant this.");
        Assert.Contains(notes, n => n.Message == "Your first request asked for \"mit \"Anmeldung\"\"; the new version does not keep to it: it does not contain \"Anmeldung\". Please check whether your change meant this.");
        Assert.Contains(notes, n => n.Message == "Your first request asked for \"ohne Platzhalter\"; the new version does not keep to it: it has placeholders. Please check whether your change meant this.");
    }

    [Fact]
    public void In_a_change_the_placeholders_to_fill_in_and_an_empty_text_stay_as_they_are()
    {
        const string draft = "Liebe Eltern, der Elternabend ist am [Datum] in der Aula der Schule.";

        Assert.Equal(DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None), DeterministicChecksReviewer.Evaluate(draft, OrderConstraints.None, revision: true));
        Assert.Equal(DraftCheckEvaluator.Evaluate("", OrderConstraints.None), DeterministicChecksReviewer.Evaluate("", OrderConstraints.None, revision: true));
        Assert.True(Assert.Single(DeterministicChecksReviewer.Evaluate("", OrderConstraints.None, revision: true), c => c.Code == DraftCheckCodes.Empty).Blocking);
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
        Assert.StartsWith("Your first request asked for \"max. 10 Wörter\"", length.Message, StringComparison.Ordinal);
        Assert.True(change.Revision);
        Assert.False(plain.Revision);
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

    [Theory]
    [InlineData(FactCheckState.Off)]
    [InlineData(FactCheckState.NotShown)]
    public void Findings_an_earlier_version_carried_over_stay_also_when_its_own_check_was_off_or_not_shown(FactCheckState state)
    {
        var earlier = Earlier(Checked(state, Office with { FromVersion = 1 }, Gone));
        var facts = new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(2, earlier));

        var report = Build(Text, facts);

        var passage = Assert.Single(report.FactCheck!.Open);
        Assert.Equal((Office.TextQuote, (int?)1), (passage.TextQuote, passage.FromVersion));
    }

    [Theory]
    [InlineData(FactCheckMode.Off, FactCheckState.Off)]
    [InlineData(FactCheckMode.Shadow, FactCheckState.NotShown)]
    public void With_the_new_check_off_or_in_shadow_mode_the_findings_of_the_earlier_version_are_kept_with_their_version(FactCheckMode mode, FactCheckState state)
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, Office));
        var shadow = new FactCheckRound(1, mode, FactCheckScope.Full, FactRoundStatus.Checked, [Office with { WhatIsWrong = "Shadow says so too." }], 1, "m", false);
        var facts = new DirectRunFacts(Mode: mode, FactRounds: mode == FactCheckMode.Off ? [] : [shadow], Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        Assert.Equal(state, report.FactCheck!.State);
        var passage = Assert.Single(report.FactCheck.Open);
        Assert.Equal((1, "Nobody said where to sign up."), (passage.FromVersion, passage.WhatIsWrong));
        var next = Build(Text, new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round()], Revision: new DirectRevision(2, report)));
        Assert.Equal(1, Assert.Single(next.FactCheck!.Open).FromVersion);
    }

    [Fact]
    public void Assumptions_of_the_writer_for_the_earlier_version_that_this_run_does_not_repeat_are_carried_as_assumptions()
    {
        var earlier = DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, new WriterNotes(["Die Anmeldefrist ist Freitag.", "Die Eltern werden gesiezt."], [], []), WriterNotesStatus.Ok)));
        var notes = new WriterNotesSplit(Text, new WriterNotes(["die Eltern werden gesiezt"], [], []), WriterNotesStatus.Ok);

        var v2 = Build(Text, new DirectRunFacts(notes, Revision: new DirectRevision(1, earlier)));
        var v3 = Build(Text, new DirectRunFacts(new WriterNotesSplit(Text, WriterNotes.Empty, WriterNotesStatus.Ok), Revision: new DirectRevision(2, v2)));

        Assert.Equal([new RunReportEarlierPoint(1, "Die Anmeldefrist ist Freitag.", EarlierPointKind.Assumption)], v2.EarlierOpenPoints);
        Assert.Equal([new RunReportEarlierPoint(1, "Die Anmeldefrist ist Freitag.", EarlierPointKind.Assumption),
                      new RunReportEarlierPoint(2, "die Eltern werden gesiezt", EarlierPointKind.Assumption)], v3.EarlierOpenPoints);
        Assert.Contains("\"kind\":\"Assumption\"", RunReportJson.Serialize(v2), StringComparison.Ordinal);
        Assert.Equal(v2.EarlierOpenPoints, RunReportJson.TryParse(RunReportJson.Serialize(v2))!.EarlierOpenPoints);
    }

    [Fact]
    public void An_open_point_and_an_assumption_with_the_same_words_are_kept_apart()
    {
        var earlier = DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, new WriterNotes(["Der Raum ist offen."], ["Der Raum ist offen."], []), WriterNotesStatus.Ok)));
        var notes = new WriterNotesSplit(Text, new WriterNotes([], ["Der Raum ist offen."], []), WriterNotesStatus.Ok);

        var report = Build(Text, new DirectRunFacts(notes, Revision: new DirectRevision(1, earlier)));

        Assert.Equal([new RunReportEarlierPoint(1, "Der Raum ist offen.", EarlierPointKind.Assumption)], report.EarlierOpenPoints);
    }

    [Fact]
    public void Notes_and_flagged_passages_of_the_earlier_version_that_could_not_be_shown_are_named_once_per_version()
    {
        var hidden = new WriterNotes([], [], []) { Hidden = 2 };
        var v1 = DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, hidden, WriterNotesStatus.Ok))) with
        {
            FactCheck = Checked(FactCheckState.OpenPoints) with { NotShown = 1 },
        };
        var oneHidden = new WriterNotesSplit(Text, new WriterNotes([], [], []) { Hidden = 1 }, WriterNotesStatus.Ok);

        var v2 = Build(Text, new DirectRunFacts(oneHidden, Revision: new DirectRevision(1, v1)));
        var v3 = Build(Text, new DirectRunFacts(new WriterNotesSplit(Text, WriterNotes.Empty, WriterNotesStatus.Ok), Revision: new DirectRevision(2, v2)));

        Assert.Equal(
            [new RunReportEarlierPoint(1, "2 more notes of the writer could not be shown because they hold a link, an address, or account or access details.", EarlierPointKind.Note),
             new RunReportEarlierPoint(1, "1 flagged passage could not be checked or listed.", EarlierPointKind.Note)],
            v2.EarlierOpenPoints);
        Assert.Equal(
            [.. v2.EarlierOpenPoints!,
             new RunReportEarlierPoint(2, "1 more note of the writer could not be shown because it holds a link, an address, or account or access details.", EarlierPointKind.Note)],
            v3.EarlierOpenPoints);
    }

    [Fact]
    public void An_earlier_version_without_a_report_leaves_a_fixed_open_point_and_one_that_cannot_be_read_a_note()
    {
        var none = Build(Text, new DirectRunFacts(Revision: new DirectRevision(3, null)));
        var unreadable = Build(Text, new DirectRunFacts(Revision: new DirectRevision(3, null, ReportUnreadable: true)));

        Assert.Equal([new RunReportEarlierPoint(3, "The checks of version 3 were not taken over, so anything they left open was not checked again.", EarlierPointKind.Note)],
            none.EarlierOpenPoints);
        Assert.Null(unreadable.EarlierOpenPoints);
        Assert.Contains(DirectReportBuilder.EarlierReportMissingNote, unreadable.Notes!);
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
    private static readonly FactFinding AgainstRequest = new("f4", FactFindingKind.Instruction, "Wir freuen uns auf Sie.", "Breaks the instruction.",
        "max. 60 Wörter", null, null, null, InstructionOrigin.Stated, true, QuoteMatchKind.Exact);

    private static FactMaterial ChangedMaterial(string newest) =>
        new(Brief, [], [], null, [], FactCheckScope.Full, null, [new FactChange(2, newest, Newest: true)]);

    [Fact]
    public void A_blocking_finding_of_the_earlier_version_against_an_instruction_of_the_request_is_carried_as_a_hint()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, AgainstRequest));
        var facts = new DirectRunFacts(Material: ChangedMaterial("Bitte die Uhrzeit ergänzen."), Mode: FactCheckMode.On, FactRounds: [Round()],
            Revision: new DirectRevision(1, earlier));

        var report = Build(Text, facts);

        var passage = Assert.Single(report.FactCheck!.Open);
        Assert.Equal((1, false, true), (passage.FromVersion, passage.Blocking, passage.BeforeLastChange));
        Assert.Equal("max. 60 Wörter", passage.InstructionQuote);
        var parsed = RunReportJson.TryParse(RunReportJson.Serialize(report))!;
        Assert.True(Assert.Single(parsed.FactCheck!.Open).BeforeLastChange);
        Assert.False(RunEntityWith(RunReportJson.Serialize(report)).ToRunState(DateTimeOffset.UtcNow).CarriesBlockingPoint);
    }

    [Fact]
    public void A_carried_finding_whose_instruction_the_newest_change_repeats_still_blocks()
    {
        var earlier = Earlier(Checked(FactCheckState.OpenPoints, AgainstRequest));
        var facts = new DirectRunFacts(Material: ChangedMaterial("Bitte wirklich max. 60 Wörter."), Mode: FactCheckMode.On, FactRounds: [Round()],
            Revision: new DirectRevision(1, earlier));

        var passage = Assert.Single(Build(Text, facts).FactCheck!.Open);

        Assert.Equal((true, false), (passage.Blocking, passage.BeforeLastChange));
    }

    [Fact]
    public void A_finding_that_is_not_marked_before_the_last_change_writes_no_new_field()
    {
        var report = Build(Text, new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [Round(Office)]));

        Assert.DoesNotContain("beforeLastChange", RunReportJson.Serialize(report), StringComparison.Ordinal);
        Assert.DoesNotContain("earlierNeedsCheck", RunReportJson.Serialize(report), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void An_earlier_version_without_a_readable_report_that_was_shown_as_please_check_passes_that_on(bool needsCheck, bool unreadable, bool expected)
    {
        var report = Build(Text, new DirectRunFacts(Mode: FactCheckMode.Off, Revision: new DirectRevision(1, null, ReportUnreadable: unreadable, NeedsCheck: needsCheck)));

        Assert.Equal(expected, report.EarlierNeedsCheck);
        Assert.Equal(expected, RunEntityWith(RunReportJson.Serialize(report)).ToRunState(DateTimeOffset.UtcNow).CarriesBlockingPoint);
    }

    [Fact]
    public void An_earlier_version_with_a_report_passes_on_only_what_its_report_says()
    {
        var plain = Build(Text, new DirectRunFacts(Revision: new DirectRevision(1, Earlier(), NeedsCheck: true)));
        var inherited = Build(Text, new DirectRunFacts(Revision: new DirectRevision(2, Earlier() with { EarlierNeedsCheck = true })));

        Assert.False(plain.EarlierNeedsCheck);
        Assert.True(inherited.EarlierNeedsCheck);
    }

    private static RunEntity RunEntityWith(string reportJson) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), Status = RunStatus.Completed, BriefingText = "b",
        Outcome = RunOutcome.Converged, CompletedAt = DateTimeOffset.UtcNow.AddHours(-1), FinalText = Text, OrderVersion = 2,
        RevisionHint = "Bitte die Uhrzeit ergänzen.", ReportJson = reportJson,
    };
}
