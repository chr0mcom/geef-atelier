using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Verifies that <c>RunOrchestratorService</c> marks stale <see cref="RunStatus.Running"/> runs as
/// <see cref="RunStatus.Failed"/> on startup, simulating crash-recovery behaviour.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorRecoversCrashedRunsOnStartTests(PostgresFixture fixture)
{
    /// <summary>
    /// A run that is already in Running state when the service starts (simulated crash) should be
    /// transitioned to Failed with the error message "Service restarted".
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Service_OnStart_MarksStaleRunningRunsAsFailed()
    {
        // Arrange — insert a run directly with Status=Running (crash simulation)
        await using var ctx = fixture.NewContext();
        var runId = Guid.NewGuid();
        ctx.Runs.Add(new RunEntity
        {
            Id           = runId,
            CreatedAt    = DateTimeOffset.UtcNow,
            Status       = RunStatus.Running,
            BriefingText = "Crashed run",
            ConfigJson   = "{}",
            TokensTotal  = 0,
            CostTotal    = 0m,
            StartedAt    = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await ctx.SaveChangesAsync();

        // Act — start the service (RecoverCrashedRunsAsync runs before the polling loop)
        await using var host = new OrchestratorTestHost(fixture, new FakeAnthropicClient(),
            new Geef.Atelier.Core.Configuration.OrchestratorOptions
            {
                PollingInterval   = TimeSpan.FromSeconds(60),
                MaxConcurrentRuns = 1
            });
        await host.StartAsync();

        // Give RecoverCrashedRunsAsync time to complete
        await Task.Delay(500);
        await host.StopAsync();

        // Assert
        await using var checkCtx = fixture.NewContext();
        var run = await checkCtx.Runs.FindAsync(runId);
        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Service restarted", run.ErrorMessage);
        Assert.NotNull(run.CompletedAt);
    }
}
