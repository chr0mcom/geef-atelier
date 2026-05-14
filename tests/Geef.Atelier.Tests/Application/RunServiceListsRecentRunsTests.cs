using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application;

[Collection("Postgres")]
public sealed class RunServiceListsRecentRunsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ListRunsAsync_ReturnsLimitedAndFilteredResults()
    {
        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient());
        await host.StartAsync();

        // Submit 3 runs
        var ids = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(10); // ensure distinct CreatedAt ordering
            await using var scope = host.ScopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            ids.Add(await svc.SubmitRunAsync(new SubmitRunRequest($"Briefing {i}", "{}")));
        }

        // Wait until all 3 runs are terminal
        var deadline = DateTime.UtcNow.AddSeconds(30);
        bool allDone;
        do
        {
            await Task.Delay(300);
            await using var ctx = fixture.NewContext();
            var terminalCount = ctx.Runs.Count(r => ids.Contains(r.Id)
                && (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted));
            allDone = terminalCount == 3;
        } while (!allDone && DateTime.UtcNow < deadline);

        Assert.True(allDone, "Timed out waiting for all 3 runs to reach terminal state");

        // Test limit
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            var limited = await svc.ListRunsAsync(limit: 2);
            Assert.Equal(2, limited.Count);
            // Verify CreatedAt desc ordering
            Assert.True(limited[0].CreatedAt >= limited[1].CreatedAt);
        }

        // Test status filter
        await using (var scope = host.ScopeFactory.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
            var completed = await svc.ListRunsAsync(statusFilter: RunStatus.Completed);
            Assert.All(completed, r => Assert.Equal(RunStatus.Completed, r.Status));
        }
    }
}
