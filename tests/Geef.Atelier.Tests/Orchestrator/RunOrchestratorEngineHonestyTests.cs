using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Etappe 0 / package A through the real orchestrator against Postgres: every run ends with an outcome, the last
/// findings of a non-converged run are stored exactly once, failed reviewers become warnings, and a missing or
/// broken snapshot or a crew without reviewers fails the run with a clear message instead of running a default crew.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorEngineHonestyTests(PostgresFixture fixture)
{
    private static OrchestratorOptions FastOptions(TimeSpan? preparationTimeout = null) => new()
    {
        PollingInterval = TimeSpan.FromMilliseconds(100),
        MaxConcurrentRuns = 5,
        CancellationPollingInterval = TimeSpan.FromMilliseconds(100),
        PreparationTimeout = preparationTimeout ?? TimeSpan.FromMinutes(15),
    };

    [Fact(Timeout = 30_000)]
    public async Task ConvergedRunRecordsConvergedOutcomeAndTheLastIteration()
    {
        var runId = await CreateRunAsync(TestCrewSnapshots.KlassikJson);

        var run = await RunToEndAsync(new FakeLlmClient(), runId);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.Equal(await MaxIterationAsync(runId), run.DeliveredIteration);
    }

    [Fact(Timeout = 30_000)]
    public async Task NonConvergedRunKeepsFailedStatusDeliversTheBestDraftAndStoresTheLastFindingsOnce()
    {
        var snapshot = TestCrewSnapshots.Klassik with
        {
            ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 2, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
        };
        var runId = await CreateRunAsync(TestCrewSnapshots.Serialize(snapshot));

        var run = await RunToEndAsync(new RejectingFakeLlmClient(), runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        Assert.Equal(2, run.DeliveredIteration);

        var perIteration = await FindingsPerIterationAsync(runId);
        Assert.Equal([1, 2], perIteration.Keys.Order().ToList());
        Assert.Equal(perIteration[1], perIteration[2]);
        Assert.Equal(snapshot.Reviewers.Count, perIteration[2]);
    }

    [Fact(Timeout = 30_000)]
    public async Task FailedReviewerBecomesAWarningOnTheRun()
    {
        var snapshot = TestCrewSnapshots.Klassik with
        {
            Reviewers = [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile with { SystemPrompt = FailingReviewerClient.Marker }],
        };
        var runId = await CreateRunAsync(TestCrewSnapshots.Serialize(snapshot));

        var run = await RunToEndAsync(new FailingReviewerClient(), runId);

        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        var warnings = await WarningsAsync(runId);
        var warning = Assert.Single(warnings, w => w.Code == RunWarningCodes.ReviewerFailed);
        Assert.Equal(RunWarningSources.Review, warning.Source);
        Assert.Contains($"\"{SystemCrew.ClarityProfile.DisplayName}\"", warning.Message);
        Assert.DoesNotContain(SystemCrew.ClarityProfile.Name, warning.Message);
        Assert.Equal(1, warning.Iteration);
    }

    [Theory(Timeout = 30_000)]
    [InlineData("""{"schemaVersion":3,"executor":"not-an-object","reviewers":[],"advisors":[]}""", "could not be read")]
    [InlineData("""{"schemaVersion":99,"executor":{"name":"x"},"reviewers":[],"advisors":[]}""", "unknown format")]
    [InlineData("""null""", "empty")]
    public async Task UnusableSnapshotFailsTheRunWithAClearMessageAndNeverRunsADefaultCrew(string snapshotJson, string expected)
    {
        var runId = await CreateRunAsync(snapshotJson);
        var llm = new CountingLlmClient();

        var run = await RunToEndAsync(llm, runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Contains(expected, run.ErrorMessage);
        Assert.Equal(0, llm.Calls);
        Assert.Null(await MaxIterationAsync(runId));
    }

    [Fact(Timeout = 30_000)]
    public async Task SnapshotWithoutExecutorFailsTheRunWithAClearMessage()
    {
        var runId = await CreateRunAsync("""{"schemaVersion":3,"templateName":"klassik","reviewers":[],"advisors":[],"evaluationStrategy":0}""");
        var llm = new CountingLlmClient();

        var run = await RunToEndAsync(llm, runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("incomplete", run.ErrorMessage);
        Assert.Contains("writer", run.ErrorMessage);
        Assert.Equal(0, llm.Calls);
    }

    [Fact(Timeout = 30_000)]
    public async Task CrewWithoutReviewersFailsInsteadOfAutoApproving()
    {
        var runId = await CreateRunAsync(TestCrewSnapshots.Serialize(TestCrewSnapshots.Klassik with { Reviewers = [] }));
        var llm = new CountingLlmClient();

        var run = await RunToEndAsync(llm, runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Contains("no reviewers", run.ErrorMessage);
        Assert.Equal(0, llm.Calls);
    }

    [Fact(Timeout = 30_000)]
    public async Task ProviderErrorRecordsFailedOutcome()
    {
        var runId = await CreateRunAsync(TestCrewSnapshots.KlassikJson);

        var run = await RunToEndAsync(ThrowingLlmClient.HttpError(System.Net.HttpStatusCode.Unauthorized), runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Null(run.DeliveredIteration);
    }

    [Fact(Timeout = 30_000)]
    public async Task PendingRunWithoutSnapshotIsNotStartedAndFailsAfterThePreparationTimeout()
    {
        var llm = new CountingLlmClient();
        await RetireOtherPendingRunsAsync(Guid.Empty);
        await using var host = new OrchestratorTestHost(fixture, llm, FastOptions(TimeSpan.FromMilliseconds(300)));
        await host.StartAsync();
        var runId = await CreateRunAsync(crewSnapshotJson: null);

        var run = await WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Contains("could not be prepared", run.ErrorMessage);
        Assert.Null(run.StartedAt);
        Assert.Equal(0, llm.Calls);
    }

    [Fact(Timeout = 30_000)]
    public async Task PendingRunWithoutSnapshotWaitsUntilItsSnapshotIsWritten()
    {
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions());
        await host.StartAsync();
        var runId = await CreateRunAsync(crewSnapshotJson: null);

        await Task.Delay(600);
        await using (var db = fixture.NewContext())
        {
            Assert.Equal(RunStatus.Pending, (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).Status);
            await new RunPersistenceService(db).UpdateSnapshotAsync(runId, TestCrewSnapshots.KlassikJson);
        }

        var run = await WaitForTerminalAsync(runId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
    }

    [Fact(Timeout = 30_000)]
    public async Task RestartFailsRunsThatWereStillBeingPrepared()
    {
        var runId = await CreateRunAsync(crewSnapshotJson: null);

        var run = await RunToEndAsync(new CountingLlmClient(), runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
        Assert.Contains("Service restarted", run.ErrorMessage);
    }

    [Fact(Timeout = 30_000)]
    public async Task SkippedAndFailingFinishingStepsBecomeWarnings()
    {
        var snapshot = TestCrewSnapshots.Klassik with
        {
            Finalizers =
            [
                new FinalizerProfile("skipping-step", "Skipping step", "d", FinalizerType.Transform, [], false),
                new FinalizerProfile("throwing-step", "Throwing step", "d", FinalizerType.MetadataEnrich, [], false),
                new FinalizerProfile("error-step", "Error step", "d", FinalizerType.FileExport, [], false),
            ],
        };
        var runId = await CreateRunAsync(TestCrewSnapshots.Serialize(snapshot));
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory, ScriptedFinalizerFactory>());
        await host.StartAsync();

        var run = await WaitForTerminalAsync(runId);
        var warnings = await WaitForWarningsAsync(runId, 3);

        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.FinalizerSkipped && w.Message == "The finishing step \"Skipping step\" was skipped because nothing to do.");
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.FinalizerFailed && w.Message == "The finishing step \"Throwing step\" failed.");
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.FinalizerFailed && w.Message == "The finishing step \"Error step\" failed.");
        Assert.DoesNotContain(warnings, w => w.Message.Contains("exploded") || w.Message.Contains("disk full") || w.Message.Contains("sk-or-v1"));

        await using var db = fixture.NewContext();
        var statusNotes = await db.RunArtifacts.AsNoTracking().Where(a => a.RunId == runId).Select(a => a.StatusMessage).ToListAsync();
        Assert.Contains(statusNotes, n => n != null && n.Contains("disk full"));
        Assert.DoesNotContain(statusNotes, n => n != null && n.Contains("sk-or-v1"));
        var stored = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.DoesNotContain("sk-or-v1", stored.FinalizerErrorMessage ?? string.Empty);
    }

    [Fact(Timeout = 30_000)]
    public async Task FinishingStepsThatAreNotRunAfterNonConvergenceBecomeAWarning()
    {
        var snapshot = TestCrewSnapshots.Klassik with
        {
            ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
            Finalizers = [new FinalizerProfile("export", "Export", "d", FinalizerType.FileExport, [], false)],
            RunFinalizersOnMaxAttempts = false,
        };
        var runId = await CreateRunAsync(TestCrewSnapshots.Serialize(snapshot));

        await using var host = new OrchestratorTestHost(fixture, new RejectingFakeLlmClient(), FastOptions());
        await host.StartAsync();
        var run = await WaitForTerminalAsync(runId);
        var warnings = await WaitForWarningsAsync(runId, 1);

        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.FinalizersNotRun);
    }

    private async Task<Guid> CreateRunAsync(string? crewSnapshotJson)
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync(
            "Engine honesty test briefing.", "{}", crewSnapshotJson: crewSnapshotJson, cancellationToken: CancellationToken.None);
    }

    private async Task<RunEntity> RunToEndAsync(ILlmClient llm, Guid runId, OrchestratorOptions? options = null)
    {
        await RetireOtherPendingRunsAsync(runId);
        await using var host = new OrchestratorTestHost(fixture, llm, options ?? FastOptions());
        await host.StartAsync();
        return await WaitForTerminalAsync(runId);
    }

    /// <summary>
    /// Other test classes of the shared database leave pending runs behind; the host started here would process them
    /// and its LLM call count would no longer belong to the run under test.
    /// </summary>
    private async Task RetireOtherPendingRunsAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        await db.Runs
            .Where(r => r.Status == RunStatus.Pending && r.Id != runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Aborted));
    }

    private async Task<RunEntity> WaitForTerminalAsync(Guid runId)
    {
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

    private async Task<int?> MaxIterationAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Iterations.Where(i => i.RunId == runId).MaxAsync(i => (int?)i.IterationNumber);
    }

    private async Task<Dictionary<int, int>> FindingsPerIterationAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Findings
            .Join(db.Iterations.Where(i => i.RunId == runId), f => f.IterationId, i => i.Id, (f, i) => i.IterationNumber)
            .GroupBy(n => n)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    private async Task<List<RunWarningEntity>> WaitForWarningsAsync(Guid runId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var warnings = await WarningsAsync(runId);
            if (warnings.Count >= expected || DateTime.UtcNow > deadline)
                return warnings;
            await Task.Delay(100);
        }
    }

    private async Task<List<RunWarningEntity>> WarningsAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync();
    }

    private sealed class ScriptedFinalizerFactory : IFinalizerExecutorFactory
    {
        public IFinalizerExecutor GetExecutor(FinalizerType type) => new Scripted(type);

        private sealed class Scripted(FinalizerType type) : IFinalizerExecutor
        {
            public FinalizerType Type => type;

            public Task<FinalizerExecutionResult> ExecuteAsync(FinalizerProfile profile, FinalizerExecutionContext context, CancellationToken cancellationToken) =>
                type switch
                {
                    FinalizerType.Transform => Task.FromResult(new FinalizerExecutionResult(null, null, null, profile.Name, SkipReason: "nothing to do")),
                    FinalizerType.FileExport => Task.FromResult(new FinalizerExecutionResult(null, new RunArtifact
                    {
                        Id = Guid.NewGuid(),
                        RunId = context.RunId,
                        FinalizerProfileName = profile.Name,
                        ArtifactType = ArtifactType.Status,
                        StorageUri = "error",
                        StatusMessage = "disk full, token=sk-or-v1-0123456789abcdef0123456789abcdef",
                        CreatedAt = DateTimeOffset.UtcNow,
                    }, null, profile.Name)),
                    _ => throw new InvalidOperationException("exploded with Bearer sk-or-v1-0123456789abcdef0123456789abcdef"),
                };
        }
    }

    private sealed class CountingLlmClient : ILlmClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return request.Tools is null || request.Tools.Count == 0
                ? Task.FromResult(FakeLlmClient.MakeTextResponse("draft"))
                : Task.FromResult(FakeLlmClient.MakeApproveResponse());
        }
    }

    private sealed class FailingReviewerClient : ILlmClient
    {
        public const string Marker = "FAILING-REVIEWER-MARKER";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            if (request.Tools is null || request.Tools.Count == 0)
                return Task.FromResult(FakeLlmClient.MakeTextResponse("draft"));
            if (request.SystemPrompt.Contains(Marker, StringComparison.Ordinal))
                return Task.FromResult(FakeLlmClient.MakeTextResponse("I forgot to call the tool."));
            return Task.FromResult(FakeLlmClient.MakeApproveResponse());
        }
    }
}
