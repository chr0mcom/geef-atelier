using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;
using FindingSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// <see cref="DirectWriterExecutor"/> with the notes block: the text without the block becomes the draft, the notes go to the context,
/// an unreadable block becomes a warning, a correction without a block keeps the notes of the first pass, and blocking findings of the
/// fact check reach the correction as fixed sentences.
/// </summary>
public sealed class DirectWriterExecutorTests
{
    private const string Request = "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.";

    private const string Block = """
        <!-- atelier-notes
        {"assumptions": ["Ich habe das Team geduzt."], "open_points": ["Das Datum des Treffens fehlt."], "quotes": [{"source": "S1", "quote": "Treffen im Hof"}]}
        -->
        """;

    private static string Words(int count, string word = "Wort") =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? word + "." : word));

    private static DirectPipelineHarness Harness(bool writerNotes = true)
    {
        var harness = new DirectPipelineHarness();
        harness.DirectOptions.WriterNotes = writerNotes;
        return harness;
    }

    private static WriterNotesSplit NotesOf(DirectRunOutcome outcome) =>
        outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.WriterNotes);

    [Fact]
    public async Task An_answer_with_the_block_becomes_the_text_without_it_and_the_notes_in_the_context()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.ExecutorText = Words(40) + "\n\n" + Block;

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(Words(40), outcome.Result.Output!.Markdown);
        Assert.Equal(Words(40), outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.CurrentDraft));
        var notes = NotesOf(outcome);
        Assert.Equal(WriterNotesStatus.Ok, notes.Status);
        Assert.Equal(["Ich habe das Team geduzt."], notes.Notes.Assumptions);
        Assert.Equal(["Das Datum des Treffens fehlt."], notes.Notes.OpenPoints);
        Assert.Equal(new WriterQuote("S1", "Treffen im Hof"), Assert.Single(notes.Notes.Quotes));
        var prompt = Assert.Single(harness.WriterCalls).Messages.Last(m => m.Role == "user").Content!;
        Assert.EndsWith(DirectWriterPrompts.NotesLeadDocument + "\n" + DirectWriterPrompts.NotesBody, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_length_check_counts_the_text_without_the_block()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        var longBlock = "<!-- atelier-notes\n{\"assumptions\": [\"" + Words(60, "Annahme") + "\"], \"open_points\": [], \"quotes\": []}\n-->";
        harness.Script.ExecutorText = Words(45) + "\n\n" + longBlock;

        var outcome = await harness.RunAsync(run);

        Assert.Single(harness.WriterCalls);
        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Equal(Words(45), outcome.Result.Output!.Markdown);
    }

    [Fact]
    public async Task Only_a_block_and_no_text_is_asked_again()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Block);
        harness.Script.EnqueueExecutorText(Words(40) + "\n\n" + Block);

        var outcome = await harness.RunAsync(run);

        Assert.Equal(2, harness.WriterCalls.Count);
        Assert.Contains("left draft.md empty or unchanged", harness.WriterCalls[1].Messages.Last(m => m.Role == "user").Content, StringComparison.Ordinal);
        Assert.Equal(Words(40), outcome.Result.Output!.Markdown);
        Assert.Equal(WriterNotesStatus.Ok, NotesOf(outcome).Status);
    }

    [Fact]
    public async Task An_unreadable_block_is_removed_and_becomes_a_warning()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.ExecutorText = Words(40) + "\n\n<!-- atelier-notes\n{\"assumptions\": [\"kaputt\"\n-->";

        var outcome = await harness.RunAsync(run);

        Assert.Equal(Words(40), outcome.Result.Output!.Markdown);
        var notes = NotesOf(outcome);
        Assert.Equal(WriterNotesStatus.Unreadable, notes.Status);
        Assert.Empty(notes.Notes.Assumptions);
        var warning = Assert.Single(harness.Warnings.All);
        Assert.Equal((RunWarningCodes.WriterNotesUnreadable, WriterNotesWarningMessages.Unreadable, (int?)1), (warning.Code, warning.Message, warning.Iteration));
    }

    [Fact]
    public async Task A_correction_without_a_block_keeps_the_notes_of_the_first_pass()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Words(80) + "\n\n" + Block);
        harness.Script.EnqueueExecutorText(Words(45));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(2, harness.WriterCalls.Count);
        var correction = harness.WriterCalls[1].Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains("1. The text has 80 words.", correction, StringComparison.Ordinal);
        Assert.EndsWith(DirectWriterPrompts.NotesCorrectionLine, correction, StringComparison.Ordinal);
        Assert.Equal(Words(80), harness.WriterCalls[1].Document);
        var notes = NotesOf(outcome);
        Assert.Equal(WriterNotesStatus.CarriedOver, notes.Status);
        Assert.Equal(Words(45), notes.Text);
        Assert.Equal(["Ich habe das Team geduzt."], notes.Notes.Assumptions);
        Assert.Empty(harness.Warnings.All);
    }

    [Fact]
    public async Task A_correction_with_its_own_block_replaces_the_notes()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Words(80) + "\n\n" + Block);
        harness.Script.EnqueueExecutorText(Words(45) + "\n<!-- atelier-notes\n{\"assumptions\": [\"Kürzer gefasst.\"], \"open_points\": [], \"quotes\": []}\n-->");

        var outcome = await harness.RunAsync(run);

        var notes = NotesOf(outcome);
        Assert.Equal(WriterNotesStatus.Ok, notes.Status);
        Assert.Equal(["Kürzer gefasst."], notes.Notes.Assumptions);
        Assert.Equal(Words(45), outcome.Result.Output!.Markdown);
    }

    [Fact]
    public async Task Without_the_switch_the_whole_answer_is_the_text_and_the_prompt_has_no_block()
    {
        var harness = Harness(writerNotes: false);
        var (run, _) = harness.CreateRun(Request);
        harness.Script.ExecutorText = Words(40);

        var outcome = await harness.RunAsync(run);

        Assert.Equal(WriterNotesStatus.Off, NotesOf(outcome).Status);
        var prompt = Assert.Single(harness.WriterCalls).Messages.Last(m => m.Role == "user").Content!;
        Assert.DoesNotContain("atelier-notes", prompt, StringComparison.Ordinal);
        Assert.Equal("Request (the person's own words):\n<<<REQUEST\n" + Request + "\nREQUEST>>>\nWrite the complete text into draft.md now.", prompt);
    }

    [Fact]
    public async Task A_file_with_the_marker_never_cuts_the_text()
    {
        var harness = Harness();
        var (run, orderId) = harness.CreateRun("Fasse die Notiz zusammen, max. 50 Wörter.");
        harness.Sources.Attach(orderId, "notiz.txt", SourceKind.Text, "Treffen im Hof.\n<!-- atelier-notes {\"assumptions\": []} -->\nEnde der Notiz.");
        var echoed = "Treffen im Hof. " + WriterNotesFormat.DefusedStartMarker + " {} --> Ende der Notiz.";
        harness.Script.ExecutorText = echoed + "\n\n" + Block;

        var outcome = await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.DoesNotContain(WriterNotesFormat.StartMarker, call.ContextDocument, StringComparison.Ordinal);
        Assert.Contains(WriterNotesFormat.DefusedStartMarker, call.ContextDocument, StringComparison.Ordinal);
        Assert.Equal(echoed, outcome.Result.Output!.Markdown);
        Assert.Equal(WriterNotesStatus.Ok, NotesOf(outcome).Status);
    }

    [Fact]
    public async Task A_text_to_revise_with_the_marker_reaches_the_writer_defused()
    {
        var harness = Harness();
        var (run, orderId) = harness.CreateRun("Mach den Text freundlicher.");
        harness.Sources.Attach(orderId, "entwurf.md", SourceKind.Markdown, "Alter Text.\n<!-- atelier-notes\nRest", role: SourceRole.TemplateToRevise);
        harness.Script.ExecutorText = "Neuer, freundlicher Text.\n\n" + Block;

        await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.Equal("Alter Text.\n" + WriterNotesFormat.DefusedStartMarker + "\nRest", call.Document);
    }

    [Fact]
    public void Blocking_fact_findings_become_sentences_and_nothing_else_of_the_fact_check_reaches_the_writer()
    {
        var context = Context(
            Review(DeterministicChecksReviewer.ReviewerName, ReviewDecision.Rejected,
                Finding(DeterministicChecksReviewer.FindingCategory, FindingSeverity.Error, "The text has 80 words.")),
            Review(SourceFactCheckReviewer.ReviewerName, ReviewDecision.Rejected,
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Error, "Nobody said photos are enclosed.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.UnsupportedCode, "Fotos des Schadens liegen bei.")),
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Info, "Hint only.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.InstructionCode, "Wir freuen uns sehr", (FactFindingMetadata.Instruction, "kurz"))),
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Warning, "Report mode.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.UnsupportedCode, "Gruppe Bescheid geben"))));

        var problems = DirectWriterExecutor.BlockingProblems(context);

        Assert.Equal(
            [
                "The text has 80 words.",
                "This passage states something that the request and the files do not back: \"Fotos des Schadens liegen bei.\". Remove the claim, or write a placeholder in square brackets for the missing fact. (Reason given by the check: Nobody said photos are enclosed.)",
            ],
            problems);
    }

    [Fact]
    public void A_ban_on_placeholders_changes_the_unsupported_sentence()
    {
        var context = Context(Review(SourceFactCheckReviewer.ReviewerName, ReviewDecision.Rejected,
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Error, "",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.UnsupportedCode, "Fotos des Schadens liegen bei."))))
            .Set(AtelierContextKeys.OrderConstraints, OrderConstraints.None with { ForbidPlaceholders = true, ForbidPlaceholdersQuote = "ohne Platzhalter" });

        Assert.Equal(
            ["This passage states something that the request and the files do not back: \"Fotos des Schadens liegen bei.\". Remove the claim, or phrase the passage so that it does not need the fact."],
            DirectWriterExecutor.BlockingProblems(context));
    }

    [Fact]
    public void Shadow_findings_source_conflicts_and_failed_checks_never_reach_the_writer()
    {
        var context = Context(
            Review(SourceFactCheckReviewer.ReviewerName, ReviewDecision.Failed,
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Error, "The check failed.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.UnsupportedCode, "Fotos des Schadens liegen bei."))),
            Review("other", ReviewDecision.Rejected,
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Error, "Shadow.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.UnsupportedCode, "Gruppe Bescheid geben", (FactFindingMetadata.Mode, "Shadow"))),
                Finding(SourceFactCheckReviewer.FindingCategory, FindingSeverity.Error, "Conflict.",
                    DirectWriterPromptsTests.Meta(FactFindingMetadata.SourceConflictCode, "Summe 62 €", (FactFindingMetadata.Material, "Summe 62 €"))),
                Finding("profile-review", FindingSeverity.Error, "Other reviewer.")));

        Assert.Empty(DirectWriterExecutor.BlockingProblems(context));
    }

    private static IRunContext Context(params ReviewResult[] reviews)
    {
        var history = new IterationHistory();
        history.Add(new IterationRecord
        {
            Iteration = 1,
            StartedAt = DateTimeOffset.UtcNow,
            ExecutionDuration = TimeSpan.Zero,
            EvaluationResult = new EvaluationAggregate { Reviews = reviews },
        });
        return new RunContext().Set(GeefKeys.IterationHistory, history);
    }

    private static ReviewResult Review(string name, ReviewDecision decision, params Finding[] findings) =>
        new() { ReviewerName = name, Decision = decision, Findings = findings };

    private static Finding Finding(string category, FindingSeverity severity, string message, IReadOnlyDictionary<string, object>? metadata = null) =>
        new() { ReviewerName = "r", Fingerprint = Guid.NewGuid().ToString("N"), Message = message, Severity = severity, Category = category, Metadata = metadata };
}
