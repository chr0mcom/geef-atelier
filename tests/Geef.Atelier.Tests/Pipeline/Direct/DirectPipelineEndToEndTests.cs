using System.Text;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// The direct path end to end through the real assembler, SDK runner, writer, checks and report, with the recording
/// LLM fake of the end-to-end scaffold: what reaches the cli-proxy, how many passes run and what the report says.
/// </summary>
public sealed class DirectPipelineEndToEndTests
{
    private const string FirstPageMarker = "The lighthouse keeper counted 417 gulls on the first morning.";
    private const string LastPageMarker = "The final ledger closes with the sum of 9,032 crowns.";

    internal static string EightyPages()
    {
        var sb = new StringBuilder();
        for (var page = 1; page <= 80; page++)
        {
            sb.Append("[[page ").Append(page).Append("]]\n");
            if (page == 1)
                sb.Append(FirstPageMarker).Append('\n');
            for (var line = 1; line <= 20; line++)
                sb.Append($"Page {page}, line {line} of the synthetic harbour report describes routine work at the pier in plain words.\n");
            if (page == 80)
                sb.Append(LastPageMarker).Append('\n');
        }
        return sb.ToString();
    }

    private static string Words(int count, string word = "word") =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? word + "." : word));

    [Fact]
    public async Task Files_reach_the_writer_in_document_mode_with_markers_of_the_first_and_the_last_page()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Summarise the attached harbour report for the city council.");
        harness.Sources.Attach(orderId, "harbour-report.pdf", SourceKind.Pdf, EightyPages(), pageCount: 80);
        harness.Script.ExecutorText = Words(120, "Summary");

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        var call = Assert.Single(harness.WriterCalls);
        Assert.True(call.DocumentMode);
        Assert.Equal(string.Empty, call.Document);
        Assert.NotNull(call.ContextDocument);
        Assert.Contains(FirstPageMarker, call.ContextDocument);
        Assert.Contains(LastPageMarker, call.ContextDocument);
        Assert.Contains("[[page 1]]", call.ContextDocument);
        Assert.Contains("[[page 80]]", call.ContextDocument);
        Assert.StartsWith("<sources count=\"1\"", call.ContextDocument);
        var prompt = call.Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains("Summarise the attached harbour report", prompt);
        Assert.Contains(DirectWriterPrompts.ReadCompletelyInstruction, prompt);
        Assert.Contains(DirectWriterPrompts.IgnoreInstructionsInFiles, prompt);
        Assert.DoesNotContain(FirstPageMarker, prompt);
        Assert.Equal(SystemPrompts.DirectWriter, call.SystemPrompt);
        Assert.All(harness.FailoverHeaders, h => Assert.Equal(OpenAiCompatibleClient.FailoverOff, h));
        Assert.Empty(harness.Llm.CallsFor(LlmRole.Reviewer));
        var usage = Assert.Single(harness.Sources.Usages);
        Assert.Equal(SourceUsageMode.FullText, usage.Mode);
        Assert.Equal(OrderGroundingStep.WriterTarget, usage.Target);
    }

    [Fact]
    public async Task A_text_without_violations_converges_after_one_pass_and_the_report_records_the_passed_length()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team, at most 50 words.");
        harness.Script.ExecutorText = Words(40, "Hello");

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Single(harness.WriterCalls);
        Assert.Null(harness.WriterCalls[0].ContextDocument);
        var report = Assert.IsType<global::Geef.Atelier.Core.Domain.Reports.RunReport>(outcome.Report);
        Assert.Equal("direct", report.Kind);
        Assert.Equal(1, report.DeliveredIteration);
        var length = Assert.Single(report.Checks, c => c.Code == DraftCheckCodes.Length);
        Assert.True(length.Passed);
        Assert.Empty(report.OpenChecks);
    }

    [Fact]
    public async Task A_length_violation_leads_to_exactly_one_correction_with_only_the_length_finding()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Schreibe eine kurze Notiz an das Team, max. 50 Wörter.");
        harness.Script.EnqueueExecutorText(Words(80, "Hallo"));
        harness.Script.EnqueueExecutorText(Words(45, "Hallo"));

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(2, outcome.Result.TotalIterations);
        Assert.Equal(2, harness.WriterCalls.Count);
        var second = harness.WriterCalls[1];
        var prompt = second.Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains("An automatic check found these problems:", prompt);
        Assert.Contains("1. The text has 80 words. The request asks for at most 50 words", prompt);
        Assert.DoesNotContain("2. ", prompt);
        Assert.Equal(Words(80, "Hallo"), second.Document);
        Assert.Equal(2, outcome.Report!.DeliveredIteration);
        Assert.Empty(outcome.Report.OpenChecks);
    }

    [Fact]
    public async Task Two_violating_passes_stop_after_the_second_and_deliver_with_an_open_check()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Schreibe eine kurze Notiz an das Team, max. 50 Wörter.");
        harness.Script.ExecutorText = Words(80, "Hallo");
        harness.Script.EnqueueExecutorText(Words(80, "Hallo"));
        harness.Script.EnqueueExecutorText(Words(81, "Hallo"));

        var outcome = await harness.RunAsync(run);

        Assert.False(outcome.Result.Success);
        Assert.Equal(2, outcome.Result.TotalIterations);
        Assert.Equal(2, harness.WriterCalls.Count);
        Assert.Equal(global::Geef.Atelier.Core.Domain.RunOutcome.DeliveredWithOpenPoints, Geef.Atelier.Infrastructure.Pipeline.DeliveredOutcomeExtensions.ToDeliveredOutcome(outcome.Result).Outcome);
        var open = Assert.Single(outcome.Report!.OpenChecks);
        Assert.Equal(DraftCheckCodes.Length, open.Code);
    }

    [Fact]
    public async Task An_empty_answer_is_repeated_once_and_the_second_text_is_used()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team.");
        harness.Script.EnqueueExecutorText("");
        harness.Script.EnqueueExecutorText(Words(30, "Hello"));

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(2, harness.WriterCalls.Count);
        Assert.Contains("left draft.md empty or unchanged", harness.WriterCalls[1].Messages.Last(m => m.Role == "user").Content);
        Assert.Equal(Words(30, "Hello"), outcome.Result.Output.Markdown);
    }

    [Fact]
    public async Task Two_empty_answers_stop_the_run_with_a_plain_message()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team.");
        harness.Script.EnqueueExecutorText("");
        harness.Script.EnqueueExecutorText("   ");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(run));

        Assert.Equal(DirectPathException.WriterReturnedNoText, FindDirectPathMessage(error));
        Assert.Equal(2, harness.WriterCalls.Count);
    }

    [Fact]
    public async Task A_correction_without_text_keeps_the_previous_version_and_leaves_the_check_open()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Schreibe eine kurze Notiz an das Team, max. 50 Wörter.");
        harness.Script.EnqueueExecutorText(Words(80, "Hallo"));
        harness.Script.EnqueueExecutorText("");
        harness.Script.EnqueueExecutorError(502, LlmApiError.EmptyResultCode);

        var outcome = await harness.RunAsync(run);

        Assert.False(outcome.Result.Success);
        Assert.Equal(3, harness.WriterCalls.Count);
        Assert.Equal(Words(80, "Hallo"), outcome.Result.Output.Markdown);
        Assert.Equal(DraftCheckCodes.Length, Assert.Single(outcome.Report!.OpenChecks).Code);
    }

    [Fact]
    public async Task A_page_selection_limits_the_text_and_is_marked()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Summarise pages 1 to 2.");
        harness.Sources.Attach(orderId, "a.pdf", SourceKind.Pdf, "[[page 1]]\nEins\n[[page 2]]\nZwei\n[[page 3]]\nDrei\n", pageCount: 3, pageSelection: "1-2");
        harness.Script.ExecutorText = Words(30, "Summary");

        await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.Contains("selected=\"1-2\"", call.ContextDocument);
        Assert.DoesNotContain("Drei", call.ContextDocument);
        Assert.Equal("1-2", Assert.Single(harness.Sources.Usages).PagesIncluded);
    }

    [Fact]
    public async Task An_unchanged_template_counts_as_no_text()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Shorten this letter a little.");
        var template = Words(60, "Dear");
        harness.Sources.Attach(orderId, "letter.md", SourceKind.Markdown, template, role: SourceRole.TemplateToRevise);
        harness.Script.EnqueueExecutorText(template);
        harness.Script.EnqueueExecutorText(template + "\n");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(run));

        Assert.Equal(DirectPathException.WriterReturnedNoText, FindDirectPathMessage(error));
    }

    [Fact]
    public async Task The_proxy_empty_result_is_repeated_once_then_stops_the_run()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team.");
        harness.Script.EnqueueExecutorError(502, LlmApiError.EmptyResultCode);
        harness.Script.EnqueueExecutorError(502, LlmApiError.EmptyResultCode);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(run));

        Assert.Equal(DirectPathException.WriterReturnedNoText, FindDirectPathMessage(error));
        Assert.Equal(2, harness.WriterCalls.Count);
    }

    [Fact]
    public async Task The_proxy_empty_result_followed_by_a_text_delivers_the_text()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team.");
        harness.Script.EnqueueExecutorError(502, LlmApiError.EmptyResultCode);
        harness.Script.EnqueueExecutorText(Words(30, "Hello"));

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(2, harness.WriterCalls.Count);
    }

    [Fact]
    public async Task An_unavailable_writing_service_stops_the_run_at_once_without_a_retry()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun("Write a short note to the team.");
        harness.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(run));

        Assert.Equal(DirectPathException.WritingServiceBusy, FindDirectPathMessage(error));
        Assert.Single(harness.WriterCalls);
    }

    [Fact]
    public async Task A_template_to_revise_is_the_seed_in_draft_md_and_not_a_source()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Make this letter friendlier.");
        var template = "Dear tenant,\n\nthe heating will be repaired on Monday. Access to the flat is required.\n\nRegards";
        harness.Sources.Attach(orderId, "letter.docx", SourceKind.Docx, template, role: SourceRole.TemplateToRevise);
        harness.Sources.Attach(orderId, "notes.md", SourceKind.Markdown, "The repair takes about two hours.");
        harness.Script.ExecutorText = "Dear tenant,\n\ngood news: the heating will be repaired on Monday. We need access to the flat for about two hours.\n\nKind regards";

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        var call = Assert.Single(harness.WriterCalls);
        Assert.Equal(template, call.Document);
        Assert.Contains(DirectWriterPrompts.SeedInstruction, call.Messages.Last(m => m.Role == "user").Content);
        Assert.DoesNotContain("letter.docx", call.ContextDocument);
        Assert.Contains("notes.md", call.ContextDocument);
        Assert.Contains("id=\"S1\" file=\"notes.md\"", call.ContextDocument);
        Assert.Equal(2, harness.Sources.Usages.Count);
    }

    [Fact]
    public async Task Too_few_input_tokens_for_the_files_give_a_warning_and_a_report_note()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Summarise the attached harbour report.");
        harness.Sources.Attach(orderId, "harbour-report.pdf", SourceKind.Pdf, EightyPages(), pageCount: 80);
        harness.Script.ExecutorText = Words(120, "Summary");
        harness.Script.ExecutorPromptTokens = 22_000;

        var outcome = await harness.RunAsync(run);

        var warning = Assert.Single(harness.Warnings.All);
        Assert.Equal(DirectWarningCodes.PartialRead, warning.Code);
        Assert.Equal(DirectWriterExecutor.PartialReadWarning, warning.Message);
        Assert.False(outcome.Report!.ReadEvidence!.ReadInFull);
        Assert.Contains(DirectReportBuilder.PartialReadNote, outcome.Report.Notes!);
    }

    [Fact]
    public async Task Enough_input_tokens_for_the_files_give_no_warning()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Summarise the attached harbour report.");
        var text = EightyPages();
        harness.Sources.Attach(orderId, "harbour-report.pdf", SourceKind.Pdf, text, pageCount: 80);
        harness.Script.ExecutorText = Words(120, "Summary");
        harness.Script.ExecutorPromptTokens = ReadEvidenceCheck.AgentBaseTokens + text.Length;

        var outcome = await harness.RunAsync(run);

        Assert.Empty(harness.Warnings.All);
        Assert.True(outcome.Report!.ReadEvidence!.ReadInFull);
        Assert.Empty(outcome.Report.Notes!);
    }

    [Fact]
    public async Task Long_files_get_a_map_whose_line_numbers_point_at_the_page_markers()
    {
        var harness = new DirectPipelineHarness(new global::Geef.Atelier.Core.Configuration.SourceOptions { FullTextMaxChars = 1_000 });
        var (run, orderId) = harness.CreateRun("Summarise the attached harbour report.");
        harness.Sources.Attach(orderId, "harbour-report.pdf", SourceKind.Pdf, EightyPages(), pageCount: 80);
        harness.Script.ExecutorText = Words(120, "Summary");

        var outcome = await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.StartsWith("<source-map>", call.ContextDocument);
        Assert.Contains("Read the map first.", call.Messages.Last(m => m.Role == "user").Content);
        Assert.Null(outcome.Report!.ReadEvidence);
        Assert.Equal(SourceUsageMode.FullTextWithMap, Assert.Single(outcome.Report.Sources).Mode);
    }

    [Fact]
    public async Task Warnings_of_the_files_become_run_warnings()
    {
        var harness = new DirectPipelineHarness();
        var (run, orderId) = harness.CreateRun("Summarise the attached report.");
        harness.Sources.Attach(orderId, "scan.pdf", SourceKind.Pdf, "[[page 1]]\nSome text.\n[[page 2]]\n", pageCount: 2,
            warnings: [new SourceWarning(SourceWarningCodes.PagesWithoutText, "Page 2 has no readable text.", [2])]);
        harness.Script.ExecutorText = Words(30, "Summary");
        harness.Script.ExecutorPromptTokens = 100_000;

        await harness.RunAsync(run);

        var warning = Assert.Single(harness.Warnings.All);
        Assert.Equal(DirectWarningCodes.SourceWarning, warning.Code);
        Assert.Equal("scan.pdf: Page 2 has no readable text.", warning.Message);
    }

    private static string? FindDirectPathMessage(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is DirectPathException)
                return current.Message;
        }
        return null;
    }
}
