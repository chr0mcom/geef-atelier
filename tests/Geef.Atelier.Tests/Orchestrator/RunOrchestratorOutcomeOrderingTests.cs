using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// The outcome means "everything is stored" (review findings Q4, Q5, A2, T2, F3 of Etappe 0): it appears only after
/// the text and the results of the run kind are saved, never for a run whose status or text could not be written, and a
/// critical stop with a text is deliberately <see cref="RunOutcome.DeliveredWithOpenPoints"/>. Failures and delays
/// of single database writes are injected with row triggers that only match the run of the test.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorOutcomeOrderingTests(PostgresFixture fixture)
{
    private static OrchestratorOptions FastOptions() => new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(100),
        MaxConcurrentRuns = 5,
        CancellationPollingInterval = TimeSpan.FromMilliseconds(100),
        PreparationTimeout = TimeSpan.FromMinutes(15),
    };

    [Fact(Timeout = 30_000)]
    public async Task ConvergedOutcomeAppearsOnlyAfterTheRunKindResultsAreStored()
    {
        var finish = new BlockingFinishAssembler();
        var runId = await CreateRunAsync(Briefing(), TestCrewSnapshots.RunnableJson);
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IPipelineAssembler>(finish));
        await host.StartAsync();

        await finish.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(300);
        var whileFinishing = await LoadAsync(runId);
        Assert.Equal(RunStatus.Completed, whileFinishing.Status);
        Assert.Null(whileFinishing.Outcome);

        finish.Release.SetResult();
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
    }

    [Fact(Timeout = 30_000)]
    public async Task BestEffortOutcomeAppearsOnlyAfterTheRunKindResultsAreStored()
    {
        var finish = new BlockingFinishAssembler();
        var runId = await CreateRunAsync(Briefing(), NonConvergingSnapshotJson());
        await using var host = new OrchestratorTestHost(fixture, new RejectingFakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IPipelineAssembler>(finish));
        await host.StartAsync();

        await finish.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(300);
        var whileFinishing = await LoadAsync(runId);
        Assert.Equal(RunStatus.Failed, whileFinishing.Status);
        Assert.False(string.IsNullOrWhiteSpace(whileFinishing.FinalText));
        Assert.Null(whileFinishing.Outcome);

        finish.Release.SetResult();
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
    }

    [Fact(Timeout = 30_000)]
    public async Task BestEffortOutcomeAppearsOnlyAfterTheDraftIsStored()
    {
        var briefing = Briefing();
        var runId = await CreateRunAsync(briefing, NonConvergingSnapshotJson());
        await using var trigger = await RunRowTrigger.CreateAsync(fixture, briefing,
            BestEffortDraftWrite,
            "PERFORM pg_sleep(1.5); RETURN NEW;");
        await using var host = new OrchestratorTestHost(fixture, new RejectingFakeLlmClient(), FastOptions());
        await host.StartAsync();

        var firstWithOutcome = await WaitForOutcomeAsync(runId, pollMs: 20);

        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, firstWithOutcome.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(firstWithOutcome.FinalText));
        Assert.NotNull(firstWithOutcome.WordCount);
    }

    [Fact(Timeout = 30_000)]
    public async Task FailedDraftWriteRecordsNoDeliveredOutcome()
    {
        var briefing = Briefing();
        var runId = await CreateRunAsync(briefing, NonConvergingSnapshotJson());
        await using var trigger = await RunRowTrigger.CreateAsync(fixture, briefing,
            BestEffortDraftWrite,
            "RAISE EXCEPTION 'injected draft write failure';");
        await using var host = new OrchestratorTestHost(fixture, new RejectingFakeLlmClient(), FastOptions());
        await host.StartAsync();

        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        var warning = Assert.Single(await WarningsAsync(runId), w => w.Code == RunWarningCodes.ResultNotSaved);
        Assert.Equal(RunOrchestratorService.ResultNotSavedMessage, warning.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task LostCompletionWriteNeverClaimsAConvergedResult()
    {
        var briefing = Briefing();
        var runId = await CreateRunAsync(briefing, TestCrewSnapshots.RunnableJson);
        await using var trigger = await RunRowTrigger.CreateAsync(fixture, briefing,
            """NEW."Status" = 'Completed' AND OLD."Status" <> 'Completed'""",
            "RAISE EXCEPTION 'injected completion write failure';");
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions());
        await host.StartAsync();

        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Equal(RunOrchestratorService.ResultNotSavedMessage, run.ErrorMessage);
        Assert.Contains(await WarningsAsync(runId), w => w.Code == RunWarningCodes.ResultNotSaved);
    }

    [Fact(Timeout = 30_000)]
    public async Task CriticalStopWithATextIsAbortedButDeliveredWithOpenPoints()
    {
        var snapshot = TestCrewSnapshots.Runnable with
        {
            ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 3, AbortOnCritical: true, DetectRegression: null, StagnationThreshold: null),
        };
        var runId = await CreateRunAsync(Briefing(), TestCrewSnapshots.Serialize(snapshot));
        await using var host = new OrchestratorTestHost(fixture, new CriticalFakeLlmClient(), FastOptions());
        await host.StartAsync();

        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal(RunStatus.Aborted, run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
    }

    private static string Briefing() => $"Outcome ordering test {Guid.NewGuid():N}.";

    private const string BestEffortDraftWrite =
        "OLD.\"Status\" = 'Failed' AND OLD.\"FinalText\" IS NOT NULL AND NEW.\"Outcome\" IS NULL " +
        "AND NEW.\"TotalCostEur\" IS NOT DISTINCT FROM OLD.\"TotalCostEur\" AND NEW.\"ErrorMessage\" IS NOT DISTINCT FROM OLD.\"ErrorMessage\"";

    private static string NonConvergingSnapshotJson() => TestCrewSnapshots.Serialize(TestCrewSnapshots.Runnable with
    {
        ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
    });

    private async Task<Guid> CreateRunAsync(string briefing, string snapshotJson)
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync(briefing, crewSnapshotJson: snapshotJson);
    }

    private async Task<RunEntity> LoadAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    private async Task<RunEntity> WaitForOutcomeAsync(Guid runId, int pollMs = 100)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            var run = await LoadAsync(runId);
            if (run.Outcome is not null || DateTime.UtcNow > deadline)
                return run;
            await Task.Delay(pollMs);
        }
    }

    private async Task<List<RunWarningEntity>> WarningsAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync();
    }

    private sealed class BlockingFinishAssembler : IPipelineAssembler
    {
        private readonly TestLoopAssembler _inner = new();

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Priority => 0;

        public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) => _inner.CanAssemble(run, snapshot);

        public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context) => _inner.Assemble(context);

        public async Task OnRunFinishedAsync(PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }
}
