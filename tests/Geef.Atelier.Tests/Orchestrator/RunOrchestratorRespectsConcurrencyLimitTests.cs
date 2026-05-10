using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Verifies that <c>RunOrchestratorService</c> never exceeds <see cref="OrchestratorOptions.MaxConcurrentRuns"/>
/// active pipeline executions at the same time.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorRespectsConcurrencyLimitTests(PostgresFixture fixture)
{
    /// <summary>
    /// With MaxConcurrentRuns=2 and four Pending runs, at most two runs should be in Running state
    /// simultaneously while the others remain Pending. After unblocking all runs all four complete.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task Service_WithMaxConcurrentRuns2_NeverExceedsTwoRunningRuns()
    {
        // Arrange — create 4 Pending runs
        var runIds = new List<Guid>();
        await using var ctx = fixture.NewContext();
        var svc = new RunPersistenceService(ctx);
        for (var i = 0; i < 4; i++)
            runIds.Add(await svc.CreateRunAsync($"Concurrency run {i + 1}", "{}", CancellationToken.None));

        // Gate starts closed (0 permits) — all API calls block
        var gate = new SemaphoreSlim(0, int.MaxValue);
        var gatedClient = new GatedFakeAnthropicClient(gate);

        await using var host = new OrchestratorTestHost(fixture, gatedClient,
            new OrchestratorOptions
            {
                PollingInterval   = TimeSpan.FromMilliseconds(100),
                MaxConcurrentRuns = 2
            });
        await host.StartAsync();

        // Wait until exactly 2 runs are Running (max 10 s)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        int runningCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            await using var checkCtx = fixture.NewContext();
            runningCount = checkCtx.Runs.Count(r => runIds.Contains(r.Id) && r.Status == RunStatus.Running);
            if (runningCount >= 2) break;
        }

        Assert.True(runningCount >= 2, "Timed out waiting for 2 runs to reach Running state");

        // Assert: exactly 2 running, 2 still pending
        await using var midCtx = fixture.NewContext();
        var runningMid = midCtx.Runs.Count(r => runIds.Contains(r.Id) && r.Status == RunStatus.Running);
        var pendingMid = midCtx.Runs.Count(r => runIds.Contains(r.Id) && r.Status == RunStatus.Pending);
        Assert.Equal(2, runningMid);
        Assert.Equal(2, pendingMid);

        // Open the gate — all blocked API calls can now proceed
        gate.Release(int.MaxValue);

        // Wait until all 4 have reached a terminal state
        var completedDeadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < completedDeadline)
        {
            await Task.Delay(300);
            await using var doneCtx = fixture.NewContext();
            var done = doneCtx.Runs.Count(r => runIds.Contains(r.Id) &&
                       (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted));
            if (done == 4) break;
        }

        await using var doneGuardCtx = fixture.NewContext();
        var doneCount = doneGuardCtx.Runs.Count(r => runIds.Contains(r.Id) &&
                        (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted));
        Assert.True(doneCount == 4, $"Timed out: only {doneCount}/4 runs reached a terminal state");

        // Assert: all 4 completed successfully
        await using var finalCtx = fixture.NewContext();
        var allDone = finalCtx.Runs.Where(r => runIds.Contains(r.Id)).ToList();
        Assert.All(allDone, r => Assert.Equal(RunStatus.Completed, r.Status));
    }
}
