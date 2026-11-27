using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Tests.EndToEnd.Fakes;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// "Extra careful" with an outline (D-087) through the direct assembler, the SDK runner and the recording fake: a run of the outline
/// template makes one planning call before the first writing pass and hands the fitted outline to that pass only; the same checks and
/// the same report follow. Without a usable plan the text is written in one go with a warning. The direct template, seeded runs and
/// corrections never plan.
/// </summary>
public sealed class CarefulOutlinePipelineTests
{
    private const string Request = "Schreibe eine kurze Notiz an das Team, etwa 100 Wörter.";

    private const string Plan = """{"sections": [{"heading": "Anlass", "words": 300, "content": "Warum die Notiz"}, {"heading": "Bitte", "words": 100, "content": "Was das Team tun soll"}]}""";

    private static readonly CrewSnapshot OutlineSnapshot = DirectPipelineHarness.DirectSnapshot with { TemplateName = SystemCrew.DirectOutlineTemplateName };

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? "Wort." : "Wort"));

    private static DirectPipelineHarness Outline()
    {
        return new DirectPipelineHarness { Snapshot = OutlineSnapshot, Costs = new RunCostAccumulator() };
    }

    private static string UserPrompt(RecordedLlmCall call) => call.Messages.Last(m => m.Role == "user").Content!;

    [Fact]
    public async Task One_planning_call_comes_first_and_the_writing_pass_follows_the_fitted_outline()
    {
        var harness = Outline();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(100));

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(2, harness.WriterCalls.Count);
        var planning = harness.WriterCalls[0];
        Assert.Equal(WritingPlanPrompts.SystemPrompt, planning.SystemPrompt);
        Assert.Contains("Do not write the text yet.", UserPrompt(planning), StringComparison.Ordinal);
        Assert.Contains("about 100 words, so the word budgets should add up to about 100", UserPrompt(planning), StringComparison.Ordinal);
        Assert.True(planning.DocumentMode);
        Assert.Equal(string.Empty, planning.Document);
        var writing = UserPrompt(harness.WriterCalls[1]);
        Assert.Contains(WritingPlanPrompts.OutlineLead, writing, StringComparison.Ordinal);
        Assert.Contains("1. Anlass – about 80 words: Warum die Notiz", writing, StringComparison.Ordinal);
        Assert.Contains("2. Bitte – about 30 words: Was das Team tun soll", writing, StringComparison.Ordinal);
        Assert.Empty(harness.Warnings.All);
        Assert.NotNull(outcome.Report);
        Assert.Equal(1, outcome.Report!.DeliveredIteration);
        var costs = harness.Costs!.Flush();
        Assert.Contains(costs, c => c is { ActorName: WritingPlanner.ActorName, IterationNumber: 1 });
        Assert.Contains(costs, c => c.ActorName == SystemCrew.DirectWriterProfile.Name);
    }

    [Theory]
    [InlineData("Here is no plan, only prose.")]
    [InlineData("""{"sections": []}""")]
    public async Task Without_a_usable_plan_the_text_is_written_in_one_go_and_the_run_says_so(string answer)
    {
        var harness = Outline();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(answer);
        harness.Script.EnqueueExecutorText(Words(100));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(2, harness.WriterCalls.Count);
        Assert.DoesNotContain(WritingPlanPrompts.OutlineLead, UserPrompt(harness.WriterCalls[1]), StringComparison.Ordinal);
        var warning = Assert.Single(harness.Warnings.All);
        Assert.Equal((DirectWarningCodes.NoWritingPlan, WritingPlanner.NoPlanWarning, (int?)1), (warning.Code, warning.Message, warning.Iteration));
    }

    [Fact]
    public async Task A_planning_call_the_proxy_refuses_is_not_repeated_and_the_text_is_still_written()
    {
        var harness = Outline();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorError(503, "backend_unavailable");
        harness.Script.EnqueueExecutorText(Words(100));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(2, harness.WriterCalls.Count);
        Assert.Equal(DirectWarningCodes.NoWritingPlan, Assert.Single(harness.Warnings.All).Code);
        Assert.DoesNotContain(harness.Costs!.Flush(), c => c.ActorName == WritingPlanner.ActorName);
    }

    [Fact]
    public async Task The_correction_pass_gets_only_the_problems_and_the_plan_is_made_once()
    {
        var harness = Outline();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(200));
        harness.Script.EnqueueExecutorText(Words(100));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(3, harness.WriterCalls.Count);
        Assert.Single(harness.WriterCalls, c => c.SystemPrompt == WritingPlanPrompts.SystemPrompt);
        var correction = UserPrompt(harness.WriterCalls[2]);
        Assert.Contains("1. The text has 200 words.", correction, StringComparison.Ordinal);
        Assert.DoesNotContain(WritingPlanPrompts.OutlineLead, correction, StringComparison.Ordinal);
        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(2, outcome.Result.TotalIterations);
    }

    [Fact]
    public async Task A_run_of_the_outline_template_with_a_seed_draft_never_plans()
    {
        var harness = Outline();
        var (run, _) = harness.CreateRun(Request, seedDraft: Words(90));
        harness.Script.EnqueueExecutorText(Words(100));

        await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.NotEqual(WritingPlanPrompts.SystemPrompt, call.SystemPrompt);
        Assert.DoesNotContain(WritingPlanPrompts.OutlineLead, UserPrompt(call), StringComparison.Ordinal);
        Assert.Empty(harness.Warnings.All);
    }

    [Fact]
    public async Task A_run_of_the_direct_template_never_plans()
    {
        var harness = new DirectPipelineHarness { Costs = new RunCostAccumulator() };
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Words(100));

        var outcome = await harness.RunAsync(run);

        var call = Assert.Single(harness.WriterCalls);
        Assert.NotEqual(WritingPlanPrompts.SystemPrompt, call.SystemPrompt);
        Assert.DoesNotContain(WritingPlanPrompts.OutlineLead, UserPrompt(call), StringComparison.Ordinal);
        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Empty(harness.Warnings.All);
        Assert.DoesNotContain(harness.Costs!.Flush(), c => c.ActorName == WritingPlanner.ActorName);
    }
}
