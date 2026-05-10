using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class PostgresEventSinkHandlesCriticalAbortTests(PostgresFixture fixture)
{
    private const string Briefing = "Schreibe etwas über kritische Fehler.";

    [Fact]
    public async Task CriticalAbort_SetsStatusAborted_NotFailed()
    {
        // Arrange
        await using var db      = fixture.NewContext();
        var             svc     = new RunPersistenceService(db);
        var             runId   = await svc.CreateRunAsync(Briefing, "{}", CancellationToken.None);
        var             scopes  = fixture.NewScopeFactory();
        var             sink    = new PostgresEventSink(runId, scopes, NullLogger.Instance);

        var options = Options.Create(new LlmOptions
        {
            ApiKey       = "fake-key",
            DefaultModel = "fake-model"
        });
        var criticalClient = new CriticalFakeLlmClient();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(criticalClient, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, criticalClient, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      criticalClient, options)
            ],
            new MarkdownFinalizer(),
            additionalSinks: [sink]);

        // Act — expect ConvergenceFailedException
        await Assert.ThrowsAsync<ConvergenceFailedException>(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        // Assert
        await using var verify = fixture.NewContext();

        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Aborted, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("critical", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
        Assert.Null(run.FinalText);

        // At least one iteration with a critical finding
        var iterations = await verify.Iterations
            .Where(i => i.RunId == runId)
            .ToListAsync();
        Assert.NotEmpty(iterations);

        var findings = await verify.Findings
            .Where(f => iterations.Select(i => i.Id).Contains(f.IterationId))
            .ToListAsync();
        Assert.Contains(findings, f => f.Severity == FindingSeverity.Critical);

        // PipelineFailedEvent must be in the event log
        var events = await verify.Events
            .Where(e => e.RunId == runId)
            .Select(e => e.EventType)
            .ToListAsync();
        Assert.Contains("PipelineFailedEvent", events);
        Assert.DoesNotContain("PipelineCompletedEvent", events);
    }
}
