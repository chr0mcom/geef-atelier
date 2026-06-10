using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application;

[Collection("Postgres")]
public sealed class RunServiceCancelsRunningRunTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CancelRunAsync_WhileRunning_SetsAbortedStatus()
    {
        var gate = new SemaphoreSlim(0, int.MaxValue);
        var client = new GatedFakeLlmClient(gate);

        await using var host = new OrchestratorTestHost(fixture, client, new OrchestratorOptions
        {
            PollingInterval            = TimeSpan.FromMilliseconds(100),
            MaxConcurrentRuns          = 5,
            CancellationPollingInterval = TimeSpan.FromMilliseconds(200)
        });
        await host.StartAsync();

        Guid runId;
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            runId = await svc.SubmitRunAsync(new SubmitRunRequest("Cancel test briefing", "{}"));
        }

        // Wait until Running (precondition guard)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        RunStatus status;
        do
        {
            await Task.Delay(100);
            await using var ctx = fixture.NewContext();
            status = (await ctx.Runs.FindAsync(runId))?.Status ?? RunStatus.Pending;
        } while (status == RunStatus.Pending && DateTime.UtcNow < deadline);

        await using (var guardCtx = fixture.NewContext())
        {
            var guardRun = await guardCtx.Runs.FindAsync(runId);
            Assert.True(guardRun?.Status == RunStatus.Running,
                $"Precondition: expected run to be Running before CancelRunAsync, got {guardRun?.Status}");
        }

        // Cancel via IRunService
        bool cancelResult;
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            cancelResult = await svc.CancelRunAsync(runId, requestingUsername: null);
        }
        Assert.True(cancelResult, "CancelRunAsync should return true for a Running run");

        // Verify DB flag
        await using (var ctx = fixture.NewContext())
        {
            var run = await ctx.Runs.FindAsync(runId);
            Assert.True(run?.CancellationRequested, "CancellationRequested should be true in DB");
        }

        // The watcher polls every 200ms and will cancel the CTS once it detects the DB flag.
        // gate.WaitAsync(ct) in GatedFakeLlmClient throws OCE when the CTS is cancelled,
        // so the pipeline aborts without needing an explicit gate release.
        // Poll until status leaves Running, then release the gate as defensive cleanup.
        var abortDeadline = DateTime.UtcNow.AddSeconds(15);
        RunStatus finalStatus;
        do
        {
            await Task.Delay(100);
            await using var ctx = fixture.NewContext();
            finalStatus = (await ctx.Runs.FindAsync(runId))?.Status ?? RunStatus.Running;
        } while (finalStatus == RunStatus.Running && DateTime.UtcNow < abortDeadline);

        // Release gate after status resolved: defensive cleanup for any residual waiter.
        gate.Release(int.MaxValue);

        Assert.Equal(RunStatus.Aborted, finalStatus);

        await using (var ctx = fixture.NewContext())
        {
            var run = await ctx.Runs.FindAsync(runId);
            Assert.Contains("Cancelled by user", run?.ErrorMessage ?? "");
        }
    }
}
