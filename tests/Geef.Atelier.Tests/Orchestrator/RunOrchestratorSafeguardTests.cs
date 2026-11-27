using System.Collections.Concurrent;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// The two safeguards around a delivered text (finding 4 of the second test review of Etappe 0): when the results of
/// the finishing steps cannot be stored, the delivered text stays, the outcome is still a delivered one and the run
/// gets a <see cref="RunWarningCodes.FinalizerFailed"/> warning; a failing live-update notifier never changes the run
/// and is not treated as a failure of the run. Both are checked on the converged path and on the best-effort path.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorSafeguardTests(PostgresFixture fixture)
{
    private const string FinishingStepsNotCompletedMessage = "The finishing steps could not be completed.";

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
    public async Task FailedStoringOfTheFinishingStepResultsKeepsTheDeliveredTextAndWarns(bool bestEffort)
    {
        var briefing = Briefing();
        var runId = await CreateRunAsync(briefing, SnapshotJson(bestEffort, withFinalizer: true));
        await using var trigger = await RunRowTrigger.CreateAsync(fixture, briefing,
            $"""NEW."FinalText" = '{TransformingFinalizerFactory.TransformedText}'""",
            "RAISE EXCEPTION 'injected finishing step write failure';");
        await using var host = new OrchestratorTestHost(fixture, LlmFor(bestEffort), FastOptions(),
            services => services.AddSingleton<IFinalizerExecutorFactory>(new TransformingFinalizerFactory()));
        await host.StartAsync();

        var run = await WaitForOutcomeAsync(runId);

        Assert.Equal(bestEffort ? RunOutcome.DeliveredWithOpenPoints : RunOutcome.Converged, run.Outcome);
        Assert.Equal(bestEffort ? RunStatus.Failed : RunStatus.Completed, run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        Assert.NotEqual(TransformingFinalizerFactory.TransformedText, run.FinalText);
        Assert.NotEqual(RunOrchestratorService.UnexpectedErrorMessage, run.ErrorMessage);
        var warnings = await WarningsAsync(runId);
        var warning = Assert.Single(warnings, w => w.Code == RunWarningCodes.FinalizerFailed);
        Assert.Equal(RunWarningSources.Finalizer, warning.Source);
        Assert.Equal(FinishingStepsNotCompletedMessage, warning.Message);
        Assert.DoesNotContain(warnings, w => w.Code == RunWarningCodes.ResultNotSaved);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingNotifierNeverChangesADeliveredRun(bool bestEffort)
    {
        var notifier = new FailingRunNotifier();
        var logs = new CapturingLoggerProvider();
        var runId = await CreateRunAsync(Briefing(), SnapshotJson(bestEffort, withFinalizer: false));
        var host = new OrchestratorTestHost(fixture, LlmFor(bestEffort), FastOptions(), services =>
        {
            services.AddSingleton<IRunNotifier>(notifier);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        await host.StartAsync();

        var delivered = await WaitForOutcomeAsync(runId);
        await host.StopAsync();
        await host.DisposeAsync();
        var run = await LoadAsync(runId);

        Assert.Equal(bestEffort ? RunOutcome.DeliveredWithOpenPoints : RunOutcome.Converged, run.Outcome);
        Assert.Equal(bestEffort ? RunStatus.Failed : RunStatus.Completed, run.Status);
        Assert.Equal(delivered.ErrorMessage, run.ErrorMessage);
        Assert.Equal(delivered.FinalText, run.FinalText);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        Assert.DoesNotContain(await WarningsAsync(runId), w => w.Code == RunWarningCodes.ResultNotSaved);
        Assert.True(notifier.Calls > 0);
        var orchestratorLogs = logs.For(typeof(RunOrchestratorService).FullName!, runId);
        Assert.Contains(orchestratorLogs, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be sent"));
        Assert.DoesNotContain(orchestratorLogs, e => e.Level >= LogLevel.Error);
    }

    private static string Briefing() => $"Orchestrator safeguard test {Guid.NewGuid():N}.";

    private static ILlmClient LlmFor(bool bestEffort) => bestEffort ? new RejectingFakeLlmClient() : new FakeLlmClient();

    private static string SnapshotJson(bool bestEffort, bool withFinalizer)
    {
        var snapshot = TestCrewSnapshots.Runnable;
        if (bestEffort)
            snapshot = snapshot with
            {
                ConvergenceOverride = new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
                RunFinalizersOnMaxAttempts = withFinalizer,
            };
        if (withFinalizer)
            snapshot = snapshot with
            {
                Finalizers = [new FinalizerProfile("transforming-step", "Transforming step", "d", FinalizerType.Transform, [], false)],
            };
        return TestCrewSnapshots.Serialize(snapshot);
    }

    private async Task<Guid> CreateRunAsync(string briefing, string snapshotJson)
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync(briefing, "{}", crewSnapshotJson: snapshotJson);
    }

    private async Task<RunEntity> LoadAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    private async Task<RunEntity> WaitForOutcomeAsync(Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            var run = await LoadAsync(runId);
            if (run.Outcome is not null || DateTime.UtcNow > deadline)
                return run;
            await Task.Delay(100);
        }
    }

    private async Task<List<RunWarningEntity>> WarningsAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync();
    }

    private sealed class TransformingFinalizerFactory : IFinalizerExecutorFactory
    {
        public const string TransformedText = "Text after the transforming step.";

        public IFinalizerExecutor GetExecutor(FinalizerType type) => new Transforming(type);

        private sealed class Transforming(FinalizerType type) : IFinalizerExecutor
        {
            public FinalizerType Type => type;

            public Task<FinalizerExecutionResult> ExecuteAsync(FinalizerProfile profile, FinalizerExecutionContext context, CancellationToken cancellationToken) =>
                Task.FromResult(new FinalizerExecutionResult(TransformedText, null, null, profile.Name));
        }
    }

    private sealed class FailingRunNotifier : IRunNotifier
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Live update channel is down.");
        }

    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> For(string category, Guid runId) =>
            _entries.Where(e => e.Category == category && e.Message.Contains(runId.ToString())).ToList();

        public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capturing(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner._entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
