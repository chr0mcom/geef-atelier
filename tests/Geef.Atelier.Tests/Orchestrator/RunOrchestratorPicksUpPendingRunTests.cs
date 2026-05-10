using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// End-to-end test verifying that <c>RunOrchestratorService</c> picks up a Pending run and
/// drives it through the full Geef pipeline to <see cref="RunStatus.Completed"/>.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorPicksUpPendingRunTests(PostgresFixture fixture)
{
    private const string Briefing = "Teste ob der Orchestrator Pending-Runs abholt.";

    /// <summary>
    /// The orchestrator service should detect a Pending run, execute the pipeline, and persist
    /// the Completed status along with iterations, findings, and events.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Service_PicksUpPendingRun_AndCompletesIt()
    {
        // Arrange — create a Pending run
        await using var ctx = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync(Briefing, "{}", CancellationToken.None);

        // Act — start the orchestrator
        await using var host = new OrchestratorTestHost(fixture, new FakeAnthropicClient());
        await host.StartAsync();

        // Wait until Status=Completed (max 25 seconds)
        RunEntity? run = null;
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            await using var checkCtx = fixture.NewContext();
            run = await checkCtx.Runs.FindAsync(runId);
            if (run?.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted)
                break;
        }

        // Assert run reached terminal state
        Assert.NotNull(run);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.FinalText);
        Assert.True(run.TokensTotal > 0);

        await using var finalCtx = fixture.NewContext();

        var iterations = finalCtx.Iterations
            .Where(i => i.RunId == runId)
            .ToList();
        Assert.True(iterations.Count >= 2);

        var findings = finalCtx.Findings
            .Join(finalCtx.Iterations.Where(i => i.RunId == runId),
                  f => f.IterationId, i => i.Id, (f, _) => f)
            .ToList();
        Assert.NotEmpty(findings);

        var events = finalCtx.Events.Where(e => e.RunId == runId).ToList();
        Assert.True(events.Count >= 6);
    }
}
