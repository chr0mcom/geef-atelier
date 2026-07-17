using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Web.Notifications;
using Geef.Sdk.Events;
using Geef.Sdk.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class PostgresEventSinkHandlesBestEffortNonConvergenceTests(PostgresFixture fixture)
{
    private const string Briefing = "Schreibe einen kurzen Text über EventSourcing.";

    [Fact]
    public async Task NonConvergingRun_KeepsFailedStatusAndSurfacesBestEffortDraft()
    {
        // Arrange
        await using var db     = fixture.NewContext();
        var             svc    = new RunPersistenceService(db);
        var             runId  = await svc.CreateRunAsync(Briefing, "{}", cancellationToken: CancellationToken.None);
        var             scopes = fixture.NewScopeFactory();
        var             sink   = new PostgresEventSink(runId, scopes, new NoOpRunNotifier(), NullLogger.Instance);

        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>());

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            new TestLlmClientResolver(new RejectingFakeLlmClient()),
            Options.Create(new ConvergenceOptions { MaxIterations = 2 }),
            additionalSinks: [sink]);

        // Act — best-effort mode: the runner returns instead of throwing on non-convergence.
        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        // Assert pipeline reported non-convergence with a best-effort output
        Assert.False(result.Success);
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached, result.StopReason);

        await using var verify = fixture.NewContext();

        // Run entity: the trailing PipelineCompletedEvent(Success: false) must NOT overwrite
        // the Failed status set by the PipelineFailedEvent handler, but still surface the draft.
        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Pipeline stopped: maximum iterations reached", run.ErrorMessage);
        Assert.False(string.IsNullOrEmpty(run.FinalText));
        Assert.True(run.WordCount > 0);
        Assert.NotNull(run.CompletedAt);

        // Events: both lifecycle events must be present in order (Failed before Completed).
        var events = await verify.Events
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Id)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("PipelineFailedEvent",    events);
        Assert.Contains("PipelineCompletedEvent", events);
        Assert.True(
            events.IndexOf("PipelineFailedEvent") < events.IndexOf("PipelineCompletedEvent"),
            "PipelineFailedEvent must precede the best-effort PipelineCompletedEvent.");
    }

    [Fact]
    public async Task CriticalAbortInBestEffortMode_KeepsAbortedStatusAndSurfacesBestEffortDraft()
    {
        // Arrange
        await using var db     = fixture.NewContext();
        var             svc    = new RunPersistenceService(db);
        var             runId  = await svc.CreateRunAsync(Briefing, "{}", cancellationToken: CancellationToken.None);
        var             scopes = fixture.NewScopeFactory();
        var             sink   = new PostgresEventSink(runId, scopes, new NoOpRunNotifier(), NullLogger.Instance);

        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>());

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            new TestLlmClientResolver(new CriticalFakeLlmClient()),
            Options.Create(new ConvergenceOptions { MaxIterations = 2, AbortOnCritical = true }),
            additionalSinks: [sink]);

        // Act — critical aborts also flow through the best-effort branch in Build pipelines.
        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ConvergenceDecision.AbortCriticalBlocker, result.StopReason);

        await using var verify = fixture.NewContext();

        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Aborted, run.Status);
        Assert.Equal("Aborted due to critical reviewer finding", run.ErrorMessage);
        Assert.False(string.IsNullOrEmpty(run.FinalText));
        Assert.NotNull(run.CompletedAt);

        var events = await verify.Events
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Id)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("PipelineFailedEvent",    events);
        Assert.Contains("PipelineCompletedEvent", events);
    }

    [Fact]
    public async Task LostFailedEventWrite_TerminalFallbackMarksStrandedRunningRunAsFailed()
    {
        // Arrange — simulate a transiently lost PipelineFailedEvent write: the run is still
        // Running when only the best-effort PipelineCompletedEvent(Success: false) arrives.
        await using var db     = fixture.NewContext();
        var             svc    = new RunPersistenceService(db);
        var             runId  = await svc.CreateRunAsync(Briefing, "{}", cancellationToken: CancellationToken.None);
        await db.Runs.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Running));

        var sink = new PostgresEventSink(runId, fixture.NewScopeFactory(), new NoOpRunNotifier(), NullLogger.Instance);

        // Act
        await sink.PublishAsync(
            new PipelineCompletedEvent(runId.ToString(), false, 2, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Assert — the fallback promotes the stranded run to a terminal Failed state.
        await using var verify = fixture.NewContext();
        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Pipeline stopped without full convergence", run.ErrorMessage);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task UnsuccessfulCompletedEvent_DoesNotOverwriteAlreadyPersistedAbortedStatus()
    {
        // Arrange — the PipelineFailedEvent write succeeded (Aborted persisted); the trailing
        // best-effort completion must leave the terminal status untouched.
        await using var db     = fixture.NewContext();
        var             svc    = new RunPersistenceService(db);
        var             runId  = await svc.CreateRunAsync(Briefing, "{}", cancellationToken: CancellationToken.None);
        await db.Runs.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       RunStatus.Aborted)
                .SetProperty(r => r.ErrorMessage, "Aborted due to critical reviewer finding"));

        var sink = new PostgresEventSink(runId, fixture.NewScopeFactory(), new NoOpRunNotifier(), NullLogger.Instance);

        // Act
        await sink.PublishAsync(
            new PipelineCompletedEvent(runId.ToString(), false, 2, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Assert
        await using var verify = fixture.NewContext();
        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Aborted, run.Status);
        Assert.Equal("Aborted due to critical reviewer finding", run.ErrorMessage);
    }
}
