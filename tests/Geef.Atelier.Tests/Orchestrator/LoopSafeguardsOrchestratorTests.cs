using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Geef.Sdk.Policies;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// D-085 through the real orchestrator, the standard review-loop assembler, the profile-based reviewers and Postgres: with
/// <c>Convergence:BestDraft</c> and <c>Convergence:StopWithoutProgress</c> on, a loop whose rounds stop getting better ends early and
/// stores the best round as text and delivered iteration with a stop sentence that names what was checked; with the switches off
/// the same script runs to the round limit and delivers the last round as before. A resumed run (seed draft) takes the switches too.
/// Overrides per user name (D-087) switch them on only for runs of that user.
/// </summary>
[Collection("Postgres")]
public sealed class LoopSafeguardsOrchestratorTests(PostgresFixture fixture)
{
    private static readonly int[] MajorsPerReviewerAndRound = [1, 2, 2, 2, 2, 2];

    [Fact(Timeout = 30_000)]
    public async Task With_best_draft_and_stop_on_the_loop_ends_after_two_rounds_without_progress_and_keeps_the_best_round()
    {
        var runId = await CreateRunAsync();

        var run = await RunToEndAsync(runId, new ConvergenceOptions
        {
            MaxIterations = 6,
            BestDraft = LoopSwitch.On,
            StopWithoutProgress = LoopSwitch.On,
        });

        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
        Assert.Equal(3, await IterationCountAsync(runId));
        Assert.Equal(1, run.DeliveredIteration);
        Assert.Equal(ScriptedLoopClient.DraftOf(1), run.FinalText);
        Assert.Equal(RunStopMessageExtensions.NoProgressStopMessage, run.ErrorMessage);
        Assert.Equal($"{RunStopMessageExtensions.NoProgressStopMessage} {RunStopMessageExtensions.BestVersionKeptMessage}", run.FinalizerErrorMessage);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_resumed_run_takes_the_switches_as_well()
    {
        var runId = await CreateRunAsync();
        await using (var db = fixture.NewContext())
        {
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.SeedDraftText, "Text of the run it continues."));
        }

        var run = await RunToEndAsync(runId, new ConvergenceOptions
        {
            MaxIterations = 6,
            BestDraft = LoopSwitch.On,
            StopWithoutProgress = LoopSwitch.On,
        });

        Assert.Equal(3, await IterationCountAsync(runId));
        Assert.Equal(1, run.DeliveredIteration);
        Assert.Equal(ScriptedLoopClient.DraftOf(1), run.FinalText);
        Assert.Equal(RunStopMessageExtensions.NoProgressStopMessage, run.ErrorMessage);
    }

    [Fact(Timeout = 30_000)]
    public async Task With_the_switches_off_the_same_loop_runs_to_the_round_limit_and_delivers_the_last_round()
    {
        var runId = await CreateRunAsync();

        var run = await RunToEndAsync(runId, new ConvergenceOptions { MaxIterations = 6 });

        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
        Assert.Equal(6, await IterationCountAsync(runId));
        Assert.Equal(6, run.DeliveredIteration);
        Assert.Equal(ScriptedLoopClient.DraftOf(6), run.FinalText);
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached.ToRunStopMessage(), run.ErrorMessage);
    }

    [Fact(Timeout = 30_000)]
    public async Task Overrides_for_the_owner_of_the_run_switch_the_safeguards_on_for_that_run_only()
    {
        var options = new ConvergenceOptions
        {
            MaxIterations = 6,
            BestDraftOverrides = { [MeterUser] = LoopSwitch.On },
            StopWithoutProgressOverrides = { [MeterUser] = LoopSwitch.On },
        };

        var meterRun = await RunToEndAsync(await CreateRunAsync(MeterUser), options);
        Assert.Equal(3, await IterationCountAsync(meterRun.Id));
        Assert.Equal(1, meterRun.DeliveredIteration);
        Assert.Equal(RunStopMessageExtensions.NoProgressStopMessage, meterRun.ErrorMessage);

        var otherRun = await RunToEndAsync(await CreateRunAsync("someone-else"), options);
        Assert.Equal(6, await IterationCountAsync(otherRun.Id));
        Assert.Equal(6, otherRun.DeliveredIteration);
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached.ToRunStopMessage(), otherRun.ErrorMessage);
    }

    private const string MeterUser = "meter";

    private async Task<Guid> CreateRunAsync(string? createdByUser = null)
    {
        await using var db = fixture.NewContext();
        var id = await new RunPersistenceService(db).CreateRunAsync(
            "Loop safeguard test briefing.", "{}", crewSnapshotJson: TestCrewSnapshots.KlassikJson, cancellationToken: CancellationToken.None);
        if (createdByUser is not null)
            await db.Runs.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedByUser, createdByUser));
        return id;
    }

    private async Task<RunEntity> RunToEndAsync(Guid runId, ConvergenceOptions convergence)
    {
        await using (var db = fixture.NewContext())
        {
            await db.Runs
                .Where(r => r.Status == RunStatus.Pending && r.Id != runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Aborted));
        }

        await using var host = new OrchestratorTestHost(fixture, new ScriptedLoopClient(MajorsPerReviewerAndRound),
            new OrchestratorOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(100),
                MaxConcurrentRuns = 5,
                CancellationPollingInterval = TimeSpan.FromMilliseconds(100),
            },
            services => services.AddSingleton<IOptions<ConvergenceOptions>>(Options.Create(convergence)));
        await host.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            await Task.Delay(150);
            await using var db = fixture.NewContext();
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            if ((run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted) && run.Outcome is not null)
                return run;
            if (DateTime.UtcNow > deadline)
                return run;
        }
    }

    private async Task<int> IterationCountAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Iterations.CountAsync(i => i.RunId == runId);
    }

    /// <summary>
    /// Writer calls (no tools) return "Draft of round N"; each reviewer call returns the scripted number of major findings for the
    /// current round, with new texts every round.
    /// </summary>
    private sealed class ScriptedLoopClient(int[] majorsPerRound) : ILlmClient
    {
        private int _round;
        private int _reviewCalls;

        public static string DraftOf(int round) => $"Draft of round {round}.";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            if (request.Tools is null || request.Tools.Count == 0)
                return Task.FromResult(FakeLlmClient.MakeTextResponse(DraftOf(Interlocked.Increment(ref _round))));

            var round = Volatile.Read(ref _round);
            var call = Interlocked.Increment(ref _reviewCalls);
            var count = majorsPerRound[Math.Min(round, majorsPerRound.Length) - 1];
            var findings = string.Join(",", Enumerable.Range(1, count)
                .Select(i => $$"""{"severity":"major","message":"Point {{i}} of round {{round}}, call {{call}}."}"""));
            return Task.FromResult(new LlmResponse
            {
                Text = "",
                FinishReason = "tool_calls",
                ToolName = "submit_review",
                ToolArgumentsJson = $$"""{"approved":false,"findings":[{{findings}}]}""",
                TokenUsage = new LlmTokenUsage { InputTokens = 8, OutputTokens = 15 },
            });
        }
    }
}
