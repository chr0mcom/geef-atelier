using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application;

[Collection("Postgres")]
public sealed class RunServiceSubmitsAndQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SubmitAsync_ThenQueryAsync_ReturnsCompletedRunWithResults()
    {
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient());
        await host.StartAsync();

        Guid runId;
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            runId = await svc.SubmitRunAsync(new SubmitRunRequest("Test briefing for E2E run", "{}"));
        }

        // Verify run was persisted with correct briefing text (may already be Running due to fast polling).
        await using (var ctx = fixture.NewContext())
        {
            var run = await ctx.Runs.FindAsync(runId);
            Assert.NotNull(run);
            Assert.True(run.Status == RunStatus.Pending || run.Status == RunStatus.Running,
                $"Expected Pending or Running immediately after submit, got {run.Status}");
            Assert.Equal("Test briefing for E2E run", run.BriefingText);
        }

        // Wait for Completed
        var deadline = DateTime.UtcNow.AddSeconds(15);
        RunStatus finalStatus;
        do
        {
            await Task.Delay(200);
            await using var ctx = fixture.NewContext();
            var run = await ctx.Runs.FindAsync(runId);
            finalStatus = run?.Status ?? RunStatus.Pending;
        } while ((finalStatus == RunStatus.Pending || finalStatus == RunStatus.Running) && DateTime.UtcNow < deadline);

        Assert.True(finalStatus == RunStatus.Completed, $"Expected Completed but got {finalStatus}");

        // Query via IRunService
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            var result = await svc.GetRunAsync(runId, requestingUsername: null);
            Assert.NotNull(result);
            Assert.Equal(RunStatus.Completed, result.Status);
            Assert.NotNull(result.FinalText);
            Assert.True(result.TokensTotal > 0);
        }
    }
}
