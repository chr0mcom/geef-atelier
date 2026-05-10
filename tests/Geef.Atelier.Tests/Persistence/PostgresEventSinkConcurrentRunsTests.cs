using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class PostgresEventSinkConcurrentRunsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoConcurrentRuns_NoCrossContamination()
    {
        // Arrange two independent runs
        await using var db1   = fixture.NewContext();
        var             svc1  = new RunPersistenceService(db1);
        var             runId1 = await svc1.CreateRunAsync("Brief A", "{}", CancellationToken.None);

        await using var db2   = fixture.NewContext();
        var             svc2  = new RunPersistenceService(db2);
        var             runId2 = await svc2.CreateRunAsync("Brief B", "{}", CancellationToken.None);

        var scopes  = fixture.NewScopeFactory();
        var sink1   = new PostgresEventSink(runId1, scopes, NullLogger.Instance);
        var sink2   = new PostgresEventSink(runId2, scopes, NullLogger.Instance);

        var options = Options.Create(new AnthropicOptions
        {
            ApiKey        = "fake-key",
            ExecutorModel = "fake-model",
            ReviewerModel = "fake-model"
        });

        var fakeClient1 = new FakeAnthropicClient();
        var runner1 = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(fakeClient1, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, fakeClient1, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      fakeClient1, options)
            ],
            new MarkdownFinalizer(),
            additionalSinks: [sink1]);

        var fakeClient2 = new FakeAnthropicClient();
        var runner2 = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(fakeClient2, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, fakeClient2, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      fakeClient2, options)
            ],
            new MarkdownFinalizer(),
            additionalSinks: [sink2]);

        // Act — run both pipelines in parallel
        await Task.WhenAll(
            runner1.RunAsync("Brief A", CancellationToken.None),
            runner2.RunAsync("Brief B", CancellationToken.None));

        // Assert — no cross-contamination
        await using var verify = fixture.NewContext();

        var run1 = await verify.Runs.SingleAsync(r => r.Id == runId1);
        var run2 = await verify.Runs.SingleAsync(r => r.Id == runId2);
        Assert.Equal(RunStatus.Completed, run1.Status);
        Assert.Equal(RunStatus.Completed, run2.Status);

        var events1 = await verify.Events.Where(e => e.RunId == runId1).ToListAsync();
        var events2 = await verify.Events.Where(e => e.RunId == runId2).ToListAsync();
        Assert.NotEmpty(events1);
        Assert.NotEmpty(events2);

        // All events in each set belong only to that run
        Assert.All(events1, e => Assert.Equal(runId1, e.RunId));
        Assert.All(events2, e => Assert.Equal(runId2, e.RunId));

        var iters1 = await verify.Iterations.Where(i => i.RunId == runId1).ToListAsync();
        var iters2 = await verify.Iterations.Where(i => i.RunId == runId2).ToListAsync();
        Assert.Equal(2, iters1.Count);
        Assert.Equal(2, iters2.Count);
    }
}
