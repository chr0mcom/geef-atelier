using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Funktional 2 (Etappe 0, review round 2): a user cancel or a service stop while a finishing step runs is not a
/// failure of that step. The run ends as Aborted without a "finishing step failed" warning and without an error
/// artifact, whether the executor lets the cancellation through or swallows it and returns an error result. This holds
/// on the converged path and on the best-effort path (finishing steps after the last round without approval). The
/// results of steps that finished before the cancellation (changed text, costs) are kept; the status of a converged
/// run stays Completed, only the outcome says Aborted.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorFinalizerCancellationTests(PostgresFixture fixture)
{
    private static OrchestratorOptions FastOptions() => new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(100),
        MaxConcurrentRuns = 5,
        CancellationPollingInterval = TimeSpan.FromMilliseconds(100),
        PreparationTimeout = TimeSpan.FromMinutes(15),
    };

    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserCancelDuringAFinishingStepIsNoStepFailure(bool executorSwallowsCancellation)
    {
        var finalizers = new WaitingFinalizerFactory(executorSwallowsCancellation);
        var runId = await CreateRunAsync();
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory>(finalizers));
        await host.StartAsync();
        await finalizers.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await using (var db = fixture.NewContext())
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancellationRequested, true));
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Aborted, run.Outcome);
        Assert.True(finalizers.SawCancellation);
        await AssertNoStepFailureAsync(runId);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserCancelDuringAFinishingStepAfterABestEffortDraftIsNoStepFailure(bool executorSwallowsCancellation)
    {
        var finalizers = new WaitingFinalizerFactory(executorSwallowsCancellation);
        var runId = await CreateRunAsync(bestEffort: true);
        await using var host = new OrchestratorTestHost(fixture, new RejectingFakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory>(finalizers));
        await host.StartAsync();
        await finalizers.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await using (var db = fixture.NewContext())
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancellationRequested, true));
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Aborted, run.Outcome);
        Assert.Equal(RunStatus.Aborted, run.Status);
        Assert.True(finalizers.SawCancellation);
        await AssertNoStepFailureAsync(runId);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceStopDuringAFinishingStepIsNoStepFailure(bool executorSwallowsCancellation)
    {
        var finalizers = new WaitingFinalizerFactory(executorSwallowsCancellation);
        var runId = await CreateRunAsync();
        var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory>(finalizers));
        await host.StartAsync();
        await finalizers.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await host.StopAsync();
        await host.DisposeAsync();
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Aborted, run.Outcome);
        Assert.True(finalizers.SawCancellation);
        await AssertNoStepFailureAsync(runId);
    }

    [Fact(Timeout = 30_000)]
    public async Task CancelAfterAFinishedTransformKeepsItsTextAndCosts()
    {
        var finalizers = new TransformThenWaitFactory();
        var runId = await CreateRunAsync(finalizers:
        [
            new FinalizerProfile("rewrite", "Rewrite", "d", FinalizerType.Transform, [], false),
            new FinalizerProfile("deliver", "Deliver", "d", FinalizerType.FileExport, [], false),
        ]);
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory>(finalizers));
        await host.StartAsync();
        await finalizers.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await using (var db = fixture.NewContext())
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancellationRequested, true));
        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal((RunOutcome?)RunOutcome.Aborted, run.Outcome);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(TransformThenWaitFactory.TransformedText, run.FinalText);
        Assert.Equal(0.25m, run.FinalizerCostEur);
        Assert.Null(run.FinalizerErrorMessage);
        await using var verify = fixture.NewContext();
        var cost = Assert.Single(await verify.FinalizationActorCosts.AsNoTracking().Where(c => c.RunId == runId).ToListAsync());
        Assert.Equal("rewrite", cost.ActorName);
        Assert.DoesNotContain(await verify.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync(),
            w => w.Code == RunWarningCodes.FinalizerFailed);
    }

    private async Task AssertNoStepFailureAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        var warnings = await db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync();
        Assert.DoesNotContain(warnings, w => w.Code == RunWarningCodes.FinalizerFailed);
        Assert.DoesNotContain(warnings, w => w.Source == RunWarningSources.Finalizer);
        Assert.Empty(await db.RunArtifacts.AsNoTracking().Where(a => a.RunId == runId).ToListAsync());
        var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Null(run.FinalizerErrorMessage);
    }

    private async Task<Guid> CreateRunAsync(bool bestEffort = false, IReadOnlyList<FinalizerProfile>? finalizers = null)
    {
        var snapshot = TestCrewSnapshots.Klassik with
        {
            Finalizers = finalizers ?? [new FinalizerProfile("waiting-step", "Waiting step", "d", FinalizerType.Transform, [], false)],
        };
        if (bestEffort)
            snapshot = snapshot with
            {
                ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
                RunFinalizersOnMaxAttempts = true,
            };
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync(
            $"Finalizer cancellation test {Guid.NewGuid():N}.", "{}", crewSnapshotJson: TestCrewSnapshots.Serialize(snapshot));
    }

    private async Task<RunEntity> WaitForOutcomeAsync(Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            await using var db = fixture.NewContext();
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            if (run.Outcome is not null || DateTime.UtcNow > deadline)
                return run;
            await Task.Delay(100);
        }
    }

    private sealed class TransformThenWaitFactory : IFinalizerExecutorFactory
    {
        public const string TransformedText = "The rewritten text.";

        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IFinalizerExecutor GetExecutor(FinalizerType type) => new Step(this, type);

        private sealed class Step(TransformThenWaitFactory owner, FinalizerType type) : IFinalizerExecutor
        {
            public FinalizerType Type => type;

            public async Task<FinalizerExecutionResult> ExecuteAsync(FinalizerProfile profile, FinalizerExecutionContext context, CancellationToken cancellationToken)
            {
                if (type == FinalizerType.Transform)
                    return new FinalizerExecutionResult(TransformedText, null, 0.25m, profile.Name, "model", InputTokens: 10, OutputTokens: 5, ProviderName: "provider");

                owner.Waiting.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new FinalizerExecutionResult(null, null, null, profile.Name);
            }
        }
    }

    private sealed class WaitingFinalizerFactory(bool swallowCancellation) : IFinalizerExecutorFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SawCancellation { get; private set; }

        public bool SwallowCancellation { get; } = swallowCancellation;

        public IFinalizerExecutor GetExecutor(FinalizerType type) => new Waiting(this, type);

        private sealed class Waiting(WaitingFinalizerFactory owner, FinalizerType type) : IFinalizerExecutor
        {
            public FinalizerType Type => type;

            public async Task<FinalizerExecutionResult> ExecuteAsync(FinalizerProfile profile, FinalizerExecutionContext context, CancellationToken cancellationToken)
            {
                owner.Entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException) when (owner.SwallowCancellation)
                {
                    owner.SawCancellation = true;
                    return new FinalizerExecutionResult(null, new RunArtifact
                    {
                        Id = Guid.NewGuid(),
                        RunId = context.RunId,
                        FinalizerProfileName = profile.Name,
                        ArtifactType = ArtifactType.Status,
                        StorageUri = "error",
                        StatusMessage = "The step was interrupted.",
                        CreatedAt = DateTimeOffset.UtcNow,
                    }, null, profile.Name);
                }
                catch (OperationCanceledException)
                {
                    owner.SawCancellation = true;
                    throw;
                }
                return new FinalizerExecutionResult(null, null, null, profile.Name);
            }
        }
    }
}
