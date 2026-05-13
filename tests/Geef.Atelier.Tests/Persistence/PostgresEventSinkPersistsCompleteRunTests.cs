using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Web.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class PostgresEventSinkPersistsCompleteRunTests(PostgresFixture fixture)
{
    private const string Briefing = "Schreibe einen kurzen Text über EventSourcing.";

    [Fact]
    public async Task CompleteRun_PersistsIterationsAndFindingsAndEvents()
    {
        // Arrange
        await using var db      = fixture.NewContext();
        var             svc     = new RunPersistenceService(db);
        var             runId   = await svc.CreateRunAsync(Briefing, "{}", cancellationToken: CancellationToken.None);
        var             scopes  = fixture.NewScopeFactory();
        var             sink    = new PostgresEventSink(runId, scopes, new NoOpRunNotifier(), NullLogger.Instance);

        var fakeClient = new FakeLlmClient();
        var resolver   = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()),
            additionalSinks: [sink]);

        // Act
        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        // Assert pipeline succeeded
        Assert.True(result.Success);

        await using var verify = fixture.NewContext();

        // Run entity
        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.FinalText);
        Assert.False(string.IsNullOrEmpty(run.FinalText));
        Assert.NotNull(run.CompletedAt);
        Assert.NotNull(run.StartedAt);
        Assert.True(run.TokensTotal > 0);

        // Iterations: 2 expected (FakeLlmClient runs 2 iterations)
        var iterations = await verify.Iterations
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.IterationNumber)
            .ToListAsync();
        Assert.Equal(2, iterations.Count);
        Assert.All(iterations, i => Assert.False(string.IsNullOrEmpty(i.ArtifactText)));

        // Findings: ≥2 for iteration 1 (2 reviewers each reject once), 0 for iteration 2
        var iter1 = iterations.First(i => i.IterationNumber == 1);
        var iter2 = iterations.First(i => i.IterationNumber == 2);

        var findings1 = await verify.Findings
            .Where(f => f.IterationId == iter1.Id)
            .ToListAsync();
        Assert.True(findings1.Count >= 2, $"Expected ≥2 findings for iteration 1, got {findings1.Count}");

        var findings2 = await verify.Findings
            .Where(f => f.IterationId == iter2.Id)
            .ToListAsync();
        Assert.Empty(findings2);

        // Events: must contain the full lifecycle
        var events = await verify.Events
            .Where(e => e.RunId == runId)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("PipelineStartedEvent",   events);
        Assert.Contains("ExecutionCompletedEvent", events);
        Assert.Contains("EvaluationRejectedEvent", events);
        Assert.Contains("EvaluationApprovedEvent", events);
        Assert.Contains("PipelineCompletedEvent",  events);
        Assert.DoesNotContain("PipelineFailedEvent", events);
    }
}
