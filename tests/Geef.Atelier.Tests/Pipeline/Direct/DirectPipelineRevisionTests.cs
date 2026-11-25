using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// A run that changes an earlier version through the real assembler, SDK runner, writer, checks and report (harness of the direct path):
/// the change reaches the writer only as the revision instruction with the delivered text as seed, the length of the first request never
/// forces a correction, the earlier text never becomes material, the research only gets stored passages, and the report keeps the open
/// points of the earlier version. The briefing with the change block is built by hand here; the composed one comes with the order service.
/// </summary>
public sealed class DirectPipelineRevisionTests
{
    private const string Request = "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.";

    private const string Change = "- (version 2) Make the text about a third longer without adding new facts.";

    private const string Marker = "Treffen in der Kaffeeküche.";

    private const string Block = """
        <!-- atelier-notes
        {"assumptions": [], "open_points": ["Das Datum des Treffens fehlt."], "quotes": []}
        -->
        """;

    private static string Words(int count, string word = "Hallo") =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? word + "." : word));

    private static string FirstText => Marker + " " + Words(40);

    private static async Task<(DirectPipelineHarness Harness, RunEntity First)> FirstVersionAsync()
    {
        var harness = new DirectPipelineHarness();
        harness.DirectOptions.WriterNotes = true;
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(FirstText + "\n\n" + Block);
        var outcome = await harness.RunAsync(run);
        Assert.True(outcome.Result.Success);
        Assert.Equal(["Das Datum des Treffens fehlt."], outcome.Report!.WriterNotes!.OpenPoints);
        return (harness, harness.StoreFinished(run, outcome));
    }

    private static RunEntity SecondVersion(RunEntity first, string? revisionHint = Change) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Running, ConfigJson = "{}",
        BriefingText = Request + "\n\n---\nChange requested by the person (newest last; where changes differ, the newest wins):\n" + Change,
        CrewTemplateName = first.CrewTemplateName, OrderId = first.OrderId, OrderVersion = 2, ParentRunId = first.Id,
        SeedDraftText = first.FinalText, RevisionHint = revisionHint,
    };

    private sealed record RecordedLlmCallView(string Prompt, string? Document);

    private static RecordedLlmCallView Writer(DirectPipelineHarness harness, int index)
    {
        var call = harness.WriterCalls[index];
        return new RecordedLlmCallView(call.Messages.Last(m => m.Role == "user").Content!, call.Document);
    }

    [Fact]
    public async Task The_change_reaches_the_writer_with_the_delivered_text_and_the_revision_instruction_in_one_call()
    {
        var (harness, first) = await FirstVersionAsync();
        var second = SecondVersion(first);
        harness.Script.EnqueueExecutorText(Marker + " " + Words(64));

        var outcome = await harness.RunAsync(second);

        Assert.True(outcome.Result.Success);
        Assert.Equal(2, harness.WriterCalls.Count);
        var call = Writer(harness, 1);
        Assert.Equal(FirstText, call.Document);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, call.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(DirectWriterPrompts.SeedInstruction, call.Prompt, StringComparison.Ordinal);
        Assert.Contains(Change, call.Prompt, StringComparison.Ordinal);
        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Equal(Change, outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.RevisionHint));

        var length = Assert.Single(outcome.Report!.Checks, c => c.Code == DraftCheckCodes.Length);
        Assert.False(length.Passed);
        Assert.False(length.Blocking);
        Assert.Contains("your first request asked for", length.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_seed_without_a_change_keeps_the_seed_instruction_and_the_blocking_length()
    {
        var (harness, first) = await FirstVersionAsync();
        var resumed = SecondVersion(first, revisionHint: null);
        harness.Script.EnqueueExecutorText(Marker + " " + Words(64));
        harness.Script.EnqueueExecutorText(Marker + " " + Words(44));

        var outcome = await harness.RunAsync(resumed);

        Assert.True(outcome.Result.Success);
        Assert.Equal(3, harness.WriterCalls.Count);
        Assert.Contains(DirectWriterPrompts.SeedInstruction, Writer(harness, 1).Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(DirectWriterPrompts.RevisionInstruction, Writer(harness, 1).Prompt, StringComparison.Ordinal);
        Assert.False(outcome.Result.FinalContext!.TryGet(AtelierContextKeys.RevisionHint, out _));
        Assert.Null(outcome.Report!.EarlierOpenPoints);
    }

    [Fact]
    public async Task The_earlier_text_is_never_material_and_the_research_only_gets_stored_passages()
    {
        var (harness, first) = await FirstVersionAsync();
        harness.Script.EnqueueExecutorText(Marker + " " + Words(60));

        var outcome = await harness.RunAsync(SecondVersion(first));

        var material = outcome.Result.FinalContext!.GetRequired(AtelierContextKeys.FactMaterial);
        Assert.Empty(material.Files);
        Assert.DoesNotContain(Marker, material.Request, StringComparison.Ordinal);
        Assert.DoesNotContain(material.PersonAnswers, a => a.Contains(Marker, StringComparison.Ordinal));
        var research = harness.Research.Calls.Last();
        Assert.Equal((2, true), (research.OrderVersion, research.Revises));
    }

    [Fact]
    public async Task Open_points_of_the_earlier_version_the_new_one_does_not_repeat_stay_with_their_version()
    {
        var (harness, first) = await FirstVersionAsync();
        harness.Script.EnqueueExecutorText(Marker + " " + Words(60) + "\n\n" + Block.Replace("Das Datum des Treffens fehlt.", "Die Uhrzeit fehlt.", StringComparison.Ordinal));

        var outcome = await harness.RunAsync(SecondVersion(first));

        Assert.Equal(["Die Uhrzeit fehlt."], outcome.Report!.WriterNotes!.OpenPoints);
        Assert.Equal([new RunReportEarlierPoint(1, "Das Datum des Treffens fehlt.")], outcome.Report.EarlierOpenPoints);
        Assert.DoesNotContain(DirectReportBuilder.EarlierReportMissingNote, outcome.Report.Notes!);
    }

    [Fact]
    public async Task A_point_the_new_version_repeats_is_not_listed_twice()
    {
        var (harness, first) = await FirstVersionAsync();
        harness.Script.EnqueueExecutorText(Marker + " " + Words(60) + "\n\n" + Block);

        var outcome = await harness.RunAsync(SecondVersion(first));

        Assert.Equal(["Das Datum des Treffens fehlt."], outcome.Report!.WriterNotes!.OpenPoints);
        Assert.Null(outcome.Report.EarlierOpenPoints);
    }

    [Fact]
    public async Task A_failed_read_of_the_earlier_version_never_stops_the_run_and_is_named_in_the_report()
    {
        var (harness, first) = await FirstVersionAsync();
        harness.Runs.Failure = new InvalidOperationException("database gone");
        harness.Script.EnqueueExecutorText(Marker + " " + Words(60));

        var outcome = await harness.RunAsync(SecondVersion(first));

        Assert.True(outcome.Result.Success);
        Assert.NotNull(outcome.Report);
        Assert.Contains(DirectReportBuilder.EarlierReportMissingNote, outcome.Report!.Notes!);
        Assert.Null(outcome.Report.EarlierOpenPoints);
    }

    [Fact]
    public async Task An_earlier_version_without_a_report_gives_no_points_and_no_note()
    {
        var (harness, first) = await FirstVersionAsync();
        var bare = new RunEntity
        {
            Id = first.Id, CreatedAt = first.CreatedAt, Status = RunStatus.Completed, BriefingText = first.BriefingText, ConfigJson = "{}",
            OrderId = first.OrderId, OrderVersion = 1, FinalText = first.FinalText,
        };
        harness.Runs.Put(bare);
        harness.Script.EnqueueExecutorText(Marker + " " + Words(60));

        var outcome = await harness.RunAsync(SecondVersion(first));

        Assert.Null(outcome.Report!.EarlierOpenPoints);
        Assert.DoesNotContain(DirectReportBuilder.EarlierReportMissingNote, outcome.Report.Notes!);
    }
}
