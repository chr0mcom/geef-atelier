using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application;

[Collection("Postgres")]
public sealed class RunServiceCancelReturnsFalseForTerminalRunTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CancelRunAsync_ForCompletedRun_ReturnsFalse()
    {
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient());
        await host.StartAsync();

        Guid runId;
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            runId = await svc.SubmitRunAsync("Briefing for terminal cancel test", "{}");
        }

        // Wait for Completed
        var deadline = DateTime.UtcNow.AddSeconds(15);
        RunStatus finalStatus;
        do
        {
            await Task.Delay(200);
            await using var ctx = fixture.NewContext();
            finalStatus = (await ctx.Runs.FindAsync(runId))?.Status ?? RunStatus.Pending;
        } while ((finalStatus == RunStatus.Pending || finalStatus == RunStatus.Running) && DateTime.UtcNow < deadline);

        Assert.True(finalStatus == RunStatus.Completed, $"Expected Completed but got {finalStatus}");

        // Cancel should return false
        bool cancelResult;
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            cancelResult = await svc.CancelRunAsync(runId);
        }
        Assert.False(cancelResult, "CancelRunAsync should return false for a terminal run");

        // Verify CancellationRequested is NOT set
        await using (var ctx = fixture.NewContext())
        {
            var run = await ctx.Runs.FindAsync(runId);
            Assert.False(run?.CancellationRequested, "CancellationRequested should remain false");
        }
    }
}
