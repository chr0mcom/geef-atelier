using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// The report of schema 2: writer notes of the delivered round, quotes only when found word for word, the fact check of the
/// delivered text (only a check of exactly that wording counts; a failed check is not completed; a wording no check saw is "version
/// not checked"; earlier passages only when still in the text), source conflicts apart from passages, totals with their use in the
/// text and the corrections after round 1.
/// </summary>
public sealed class DirectReportBuilderTests
{
    private const string Notes = "Elternabend am 14. März. Material: Schere 22 €\nPapier 31 €\nFarbe 19 €\nSumme 62 €";
    private const string Text = "Liebe Eltern, das Material kostet 72 €. Bitte geben Sie uns in der Gruppe Bescheid.";

    private static readonly Guid SourceGuid = Guid.NewGuid();

    private static readonly PromptSource Source = new(SourceGuid, "S1", "notizen.txt", SourceKind.Text, null, null, Notes, Notes.Length);

    private static readonly FactFinding Unbacked = new("f1", FactFindingKind.Unsupported, "Bitte geben Sie uns in der Gruppe Bescheid.",
        "No way to reply in the notes.", null, null, null, null, null, true, QuoteMatchKind.Exact);

    private static readonly FactFinding OldDate = new("f2", FactFindingKind.Superseded, "Der Elternabend ist am 7. März.",
        "Moved.", null, "Elternabend am 14. März.", null, "notizen.txt", null, true, QuoteMatchKind.Exact);

    private static readonly FactFinding Conflict = new("f3", FactFindingKind.SourceConflict, null, "Totals differ.", null,
        "Summe 62 €", "Farbe 19 €", "notizen.txt", null, false, QuoteMatchKind.NotFound);

    private static FactCheckRound Round(int iteration, FactRoundStatus status, FactCheckMode mode = FactCheckMode.On, int calls = 1, params FactFinding[] open) =>
        new(iteration, mode, FactCheckScope.Full, status, open, calls, "gpt-astra-latest", false);

    private static RunReport Build(DirectRunFacts facts, int? delivered = 1, string text = Text, OrderConstraints? constraints = null) =>
        DirectReportBuilder.Build(constraints ?? OrderConstraints.None, text, [], delivered, delivered ?? 1, null, facts);

    [Fact]
    public void A_new_report_is_schema_two_and_round_trips()
    {
        var report = Build(new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, Unbacked, Conflict)]));

        var parsed = RunReportJson.TryParse(RunReportJson.Serialize(report))!;

        Assert.Equal(2, report.SchemaVersion);
        Assert.Equal(2, parsed.SchemaVersion);
        Assert.Equal(FactCheckState.OpenPoints, parsed.FactCheck!.State);
        Assert.Equal(Unbacked, Assert.Single(parsed.FactCheck.Open));
        Assert.Equal(Conflict, Assert.Single(parsed.FactCheck.SourceConflicts));
        Assert.Equal(FactCheckPrompts.Version, parsed.FactCheck.PromptVersion);
        Assert.Equal("gpt-astra-latest", parsed.FactCheck.Model);
    }

    [Fact]
    public void Writer_notes_of_the_delivered_round_and_only_quotes_found_word_for_word()
    {
        var notes = new WriterNotes(["Anrede gewählt: Liebe Eltern"], ["Die Summe 62 € in den Notizen stimmt nicht."],
        [
            new WriterQuote("S1", "Material: Schere 22 €"),
            new WriterQuote("s1", "Papier 31 €\nFarbe 19 €"),
            new WriterQuote("S1", "Schere 23 € und Papier"),
            new WriterQuote("S9", "Elternabend am 14. März"),
            new WriterQuote("S1", "kurz"),
        ]);

        var report = Build(new DirectRunFacts(new WriterNotesSplit(Text, notes, WriterNotesStatus.Ok), [Source]));

        Assert.Equal(WriterNotesStatus.Ok, report.NotesStatus);
        Assert.Equal(notes.OpenPoints, report.WriterNotes!.OpenPoints);
        Assert.Equal(2, report.SourceQuotes!.Count);
        Assert.All(report.SourceQuotes, q => Assert.True(q.Match is QuoteMatchKind.Exact or QuoteMatchKind.Normalized));
        Assert.All(report.SourceQuotes, q => Assert.Equal((SourceGuid, "notizen.txt", "S1"), (q.SourceId!.Value, q.FileName, q.SourceLabel)));
        Assert.Equal(3, report.UnverifiedQuotes);
    }

    [Fact]
    public void A_quote_from_the_text_to_revise_is_found_in_the_material()
    {
        var material = new FactMaterial("Überarbeite den Brief.", [], [], null, [new FactSource("S0", "entwurf.docx", "Sehr geehrte Frau Beispiel, wir danken Ihnen.")], FactCheckScope.Full, null);
        var notes = new WriterNotes([], [], [new WriterQuote("S0", "wir danken Ihnen")]);

        var report = Build(new DirectRunFacts(new WriterNotesSplit(Text, notes, WriterNotesStatus.Ok), [], material));

        var quote = Assert.Single(report.SourceQuotes!);
        Assert.Equal(("entwurf.docx", null), (quote.FileName, quote.SourceId));
    }

    [Theory]
    [InlineData(WriterNotesStatus.Unreadable)]
    [InlineData(WriterNotesStatus.Off)]
    public void Unreadable_or_switched_off_notes_show_nothing(WriterNotesStatus status)
    {
        var notes = new WriterNotes(["x"], ["y"], [new WriterQuote("S1", "Material: Schere 22 €")]);

        var report = Build(new DirectRunFacts(new WriterNotesSplit(Text, notes, status), [Source]));

        Assert.Same(WriterNotes.Empty, report.WriterNotes);
        Assert.Empty(report.SourceQuotes!);
    }

    [Fact]
    public void Absent_notes_leave_a_note_in_the_report()
    {
        var report = Build(new DirectRunFacts(new WriterNotesSplit(Text, WriterNotes.Empty, WriterNotesStatus.Absent)));

        Assert.Contains(DirectReportBuilder.NotesAbsentNote, report.Notes!);
    }

    [Fact]
    public void Corrections_are_blocking_findings_of_round_one_gone_in_the_delivered_round_and_fixed_checks()
    {
        var facts = new DirectRunFacts(
            Mode: FactCheckMode.On,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked, OldDate, Conflict), Round(2, FactRoundStatus.Checked, FactCheckMode.On, 1, OldDate, Conflict)],
            FirstRoundFailedChecks: [DraftCheckCodes.Length, DraftCheckCodes.Language]);
        var constraints = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, null, 50, "max. 50 Wörter", LengthKind.AtMost) };

        var report = Build(facts, delivered: 2, text: "Liebe Eltern, das Material kostet 72 €. Der Elternabend ist am 7. März.", constraints: constraints);

        Assert.Equal(
            [new RunReportCorrection(DraftCheckCodes.Length, null), new RunReportCorrection(DraftCheckCodes.Language, null), new RunReportCorrection(FactFindingMetadata.UnsupportedCode, Unbacked.TextQuote)],
            report.Corrections);
        Assert.Equal(Unbacked, Assert.Single(report.FactCheck!.Corrected));
        Assert.Equal(OldDate, Assert.Single(report.FactCheck.Open));
        Assert.Equal(2, report.FactCheck.Calls);
    }

    [Fact]
    public void Nothing_counts_as_corrected_when_round_one_is_delivered()
    {
        var facts = new DirectRunFacts(Mode: FactCheckMode.On,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked), Round(2, FactRoundStatus.Checked, FactCheckMode.On, 1)],
            FirstRoundFailedChecks: [DraftCheckCodes.Length]);

        var report = Build(facts, delivered: 1);

        Assert.Empty(report.Corrections!);
        Assert.Equal(FactCheckState.OpenPoints, report.FactCheck!.State);
    }

    [Fact]
    public void A_failed_check_of_the_delivered_round_is_not_completed_and_keeps_earlier_passages_still_in_the_text()
    {
        var facts = new DirectRunFacts(Mode: FactCheckMode.On,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked), Round(2, FactRoundStatus.Failed, FactCheckMode.On, 2, Unbacked)]);

        var report = Build(facts, delivered: 2);

        Assert.Equal(FactCheckState.NotCompleted, report.FactCheck!.State);
        Assert.Equal(Unbacked, Assert.Single(report.FactCheck.Open));
        Assert.Empty(report.FactCheck.Corrected);
        Assert.DoesNotContain(report.Corrections!, c => c.Code.StartsWith("fact.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactCheckMode.Off, 0, FactCheckState.Off)]
    [InlineData(FactCheckMode.Shadow, 1, FactCheckState.NotShown)]
    [InlineData(FactCheckMode.Report, 1, FactCheckState.Checked)]
    [InlineData(FactCheckMode.On, 2, FactCheckState.NotCompleted)]
    public void The_state_follows_mode_and_round(FactCheckMode mode, int status, FactCheckState expected)
    {
        var report = Build(new DirectRunFacts(Mode: mode, FactRounds: [Round(1, (FactRoundStatus)status, mode)]));

        Assert.Equal(expected, report.FactCheck!.State);
        Assert.Equal(mode, report.FactCheck.Mode);
    }

    [Fact]
    public void Only_a_source_conflict_open_is_still_checked()
    {
        var report = Build(new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, Conflict)]));

        Assert.Equal(FactCheckState.Checked, report.FactCheck!.State);
        Assert.Empty(report.FactCheck.Open);
        Assert.Single(report.FactCheck.SourceConflicts);
    }

    [Fact]
    public void A_mode_without_any_round_is_not_completed()
    {
        var report = Build(new DirectRunFacts(Mode: FactCheckMode.Report));

        Assert.Equal(FactCheckState.NotCompleted, report.FactCheck!.State);
    }

    [Fact]
    public void Without_mode_and_rounds_the_report_has_no_fact_check()
    {
        var report = Build(DirectRunFacts.None);

        Assert.Null(report.FactCheck);
        Assert.Equal(WriterNotesStatus.Off, report.NotesStatus);
        Assert.Empty(report.Corrections!);
    }

    [Fact]
    public void Totals_of_the_files_carry_their_use_in_the_delivered_text()
    {
        var arithmetic = SourceArithmeticCheck.Check([("S1", "notizen.txt", Notes)]);
        Assert.Single(arithmetic.Issues);

        var report = Build(new DirectRunFacts(Arithmetic: arithmetic));

        Assert.Equal(TotalUseInText.Computed, Assert.Single(report.SourceArithmetic!.Issues).UseInText);
        Assert.Equal(TotalUseInText.Stated, Assert.Single(Build(new DirectRunFacts(Arithmetic: arithmetic), text: "Das Material kostet 62 €.").SourceArithmetic!.Issues).UseInText);
    }

    [Fact]
    public void Fallback_use_and_calls_of_all_rounds_are_reported()
    {
        var rounds = new[]
        {
            Round(1, FactRoundStatus.Checked, FactCheckMode.On, 2, Unbacked) with { UsedFallbackModel = true, Model = "claude-sonnet-latest" },
            Round(2, FactRoundStatus.Checked, FactCheckMode.On, 1) with { Model = "claude-sonnet-latest" },
        };

        var report = Build(new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: rounds), delivered: 2);

        Assert.True(report.FactCheck!.UsedFallbackModel);
        Assert.Equal(3, report.FactCheck.Calls);
        Assert.Equal("claude-sonnet-latest", report.FactCheck.Model);
        Assert.Equal(FactCheckState.Checked, report.FactCheck.State);
    }

    [Fact]
    public void A_delivered_text_changed_after_its_check_is_version_not_checked_and_shows_only_earlier_passages_still_in_it()
    {
        var checkedText = Text + " Der Elternabend ist am 7. März.";
        var facts = new DirectRunFacts(Mode: FactCheckMode.Report,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, Unbacked, OldDate, Conflict) with { TextHash = FactCheckRound.HashOf(checkedText) }]);

        var report = Build(facts, delivered: 1, text: "Liebe Eltern, das Material kostet 72 €. Bitte geben Sie uns in der Gruppe Bescheid.");

        Assert.Equal(FactCheckState.VersionNotChecked, report.FactCheck!.State);
        Assert.Equal(Unbacked, Assert.Single(report.FactCheck.Open));
        Assert.Equal(Conflict with { Id = "f2" }, Assert.Single(report.FactCheck.SourceConflicts));
    }

    [Fact]
    public void Without_a_check_of_the_delivered_wording_passages_of_every_answered_round_still_in_it_stay_open()
    {
        var round1 = Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, Unbacked) with { TextHash = FactCheckRound.HashOf("eins") };
        var round2 = Round(2, FactRoundStatus.Checked, FactCheckMode.Report, 1, OldDate, Conflict) with { TextHash = FactCheckRound.HashOf("zwei") };
        var round3 = Round(3, FactRoundStatus.Failed, FactCheckMode.Report);
        var text = Text + " Der Elternabend ist am 7. März.";

        var report = Build(new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [round1, round2, round3]), delivered: 3, text: text);

        Assert.Equal(FactCheckState.NotCompleted, report.FactCheck!.State);
        Assert.Equal(2, report.FactCheck.Open.Count);
        Assert.Single(report.FactCheck.SourceConflicts);
        Assert.Empty(report.FactCheck.Corrected);
    }

    [Fact]
    public void When_an_earlier_version_is_delivered_findings_of_a_later_round_are_added_and_its_unlisted_passages_counted()
    {
        var notInDelivered = Unbacked with { Id = "f2", TextQuote = "Wir treffen uns im Raum 12." };
        var round1 = Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked) with { TextHash = FactCheckRound.HashOf(Text) };
        var round2 = Round(2, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked with { Blocking = false }, notInDelivered, Conflict)
            with { TextHash = FactCheckRound.HashOf("zwei"), NotShown = 2, CheckNotShown = 2 };

        var report = Build(new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [round1, round2]), delivered: 1);

        Assert.Equal(FactCheckState.OpenPoints, report.FactCheck!.State);
        Assert.True(Assert.Single(report.FactCheck.Open).Blocking);
        Assert.Equal(Unbacked.TextQuote, report.FactCheck.Open[0].TextQuote);
        Assert.Equal(Conflict.MaterialQuote, Assert.Single(report.FactCheck.SourceConflicts).MaterialQuote);
        Assert.Equal(2, report.FactCheck.NotShown);
    }

    [Fact]
    public void When_the_last_version_is_delivered_its_findings_stay_as_they_are()
    {
        var round1 = Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, Conflict) with { TextHash = FactCheckRound.HashOf("eins"), NotShown = 1, CheckNotShown = 1 };
        var round2 = Round(2, FactRoundStatus.Checked, FactCheckMode.On, 1, Unbacked) with { TextHash = FactCheckRound.HashOf(Text), NotShown = 1 };

        var report = Build(new DirectRunFacts(Mode: FactCheckMode.On, FactRounds: [round1, round2]), delivered: 2);

        Assert.Equal(Unbacked, Assert.Single(report.FactCheck!.Open));
        Assert.Empty(report.FactCheck.SourceConflicts);
        Assert.Equal(1, report.FactCheck.NotShown);
    }

    [Fact]
    public void A_blocking_passage_first_flagged_in_a_later_round_and_then_removed_is_named_as_removed()
    {
        var round1 = Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, Unbacked) with { TextHash = FactCheckRound.HashOf("eins") };
        var round2 = Round(2, FactRoundStatus.Checked, FactCheckMode.Report, 1, Unbacked, OldDate) with { TextHash = FactCheckRound.HashOf("zwei") };
        var round3 = Round(3, FactRoundStatus.Checked, FactCheckMode.Report, 1) with { TextHash = FactCheckRound.HashOf(Text) };

        var report = Build(new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [round1, round2, round3]), delivered: 3);

        Assert.Equal([OldDate.TextQuote], report.FactCheck!.Corrected.Select(f => f.TextQuote));
        Assert.Contains(report.Corrections!, c => c.Code == FactFindingMetadata.SupersededCode && c.QuoteBefore == OldDate.TextQuote);
    }

    [Fact]
    public void An_unchanged_passage_whose_quote_started_inside_a_word_is_never_removed_and_still_open()
    {
        const string text = "Liebe Eltern, der Beitrag ist bis zum 21. März, zu zahlen. Viele Grüße";
        var material = new FactMaterial("Elternbrief schreiben.", [], [], null, [], FactCheckScope.Full, null);
        var filtered = FactFindingFilter.Apply([new ProposedFactFinding(FactFindingKind.Unsupported, "1. März, zu zahlen.", "Not given.", null, null, null)], text, material);
        var kept = Assert.Single(filtered.Findings);
        var round1 = Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, kept) with { TextHash = FactCheckRound.HashOf(text + " Bis bald.") };

        var notChecked = Build(new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [round1, Round(2, FactRoundStatus.Failed, FactCheckMode.Report)]), delivered: 2, text: text);

        Assert.Equal("21. März, zu zahlen.", kept.TextQuote);
        Assert.Equal(FactCheckState.NotCompleted, notChecked.FactCheck!.State);
        Assert.Single(notChecked.FactCheck.Open);
        Assert.Empty(notChecked.FactCheck.Corrected);
    }

    [Fact]
    public void A_delivered_round_without_a_check_of_its_own_never_borrows_another_round()
    {
        var facts = new DirectRunFacts(Mode: FactCheckMode.Report,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1, OldDate) with { TextHash = FactCheckRound.HashOf("Der Elternabend ist am 7. März.") }]);

        var report = Build(facts, delivered: 2);

        Assert.Equal(FactCheckState.VersionNotChecked, report.FactCheck!.State);
        Assert.Empty(report.FactCheck.Open);
    }

    [Fact]
    public void The_same_wording_after_the_finalizer_still_counts_as_checked()
    {
        var facts = new DirectRunFacts(Mode: FactCheckMode.Report,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1) with { TextHash = FactCheckRound.HashOf(Text + "\n") }]);

        Assert.Equal(FactCheckState.Checked, Build(facts, text: "  " + Text.Replace(" das ", "  das ", StringComparison.Ordinal)).FactCheck!.State);
    }

    [Fact]
    public void A_passage_of_an_earlier_round_counts_only_as_whole_words_in_the_delivered_text()
    {
        var price = new FactFinding("f1", FactFindingKind.Unsupported, "5 Euro pro Kind", "x", null, null, null, null, null, true, QuoteMatchKind.Exact);
        var facts = new DirectRunFacts(Mode: FactCheckMode.On,
            FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.On, 1, price), Round(2, FactRoundStatus.Failed, FactCheckMode.On, 1)]);

        var report = Build(facts, delivered: 2, text: "Der Beitrag beträgt 15 Euro pro Kind.");

        Assert.Equal(FactCheckState.NotCompleted, report.FactCheck!.State);
        Assert.Empty(report.FactCheck.Open);
        Assert.Equal(price, Assert.Single(report.FactCheck.Corrected));
    }

    [Fact]
    public void Passages_that_could_not_be_listed_stay_counted_also_when_the_delivered_wording_was_not_checked()
    {
        var facts = new DirectRunFacts(Mode: FactCheckMode.Report, FactRounds: [Round(1, FactRoundStatus.Checked, FactCheckMode.Report, 1) with { NotShown = 3 }]);

        var checkedWording = Build(facts).FactCheck!;
        var otherWording = Build(facts with { FactRounds = [facts.FactRounds![0] with { TextHash = FactCheckRound.HashOf("anders") }] }).FactCheck!;
        var failedLater = Build(facts with { FactRounds = [facts.FactRounds![0], Round(2, FactRoundStatus.Failed, FactCheckMode.Report)] }, delivered: 2).FactCheck!;

        Assert.Equal((FactCheckState.OpenPoints, 3), (checkedWording.State, checkedWording.NotShown));
        Assert.Equal((FactCheckState.VersionNotChecked, 3), (otherWording.State, otherWording.NotShown));
        Assert.Equal((FactCheckState.NotCompleted, 3), (failedLater.State, failedLater.NotShown));
    }

    [Fact]
    public void Hidden_quotes_of_the_writer_count_as_unverified()
    {
        var notes = new WriterNotes([], [], [], Hidden: 1, HiddenQuotes: 2);

        var report = Build(new DirectRunFacts(new WriterNotesSplit(Text, notes, WriterNotesStatus.Ok), [Source]));

        Assert.Equal(2, report.UnverifiedQuotes);
        Assert.Equal(1, report.WriterNotes!.Hidden);
    }
}
