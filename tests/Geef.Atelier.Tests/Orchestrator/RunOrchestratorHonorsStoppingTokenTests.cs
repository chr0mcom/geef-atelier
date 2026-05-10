using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Verifies that stopping <c>RunOrchestratorService</c> while a run is mid-flight causes the
/// in-flight run to be persisted as <see cref="RunStatus.Aborted"/> (or Failed with a cancellation message).
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorHonorsStoppingTokenTests(PostgresFixture fixture)
{
    /// <summary>
    /// When the service is stopped while a pipeline run is blocked waiting for an LLM response,
    /// the run's status should reflect cancellation — either Aborted or Failed with a cancel-related message.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Service_WhenStopped_SetsInflightRunToAborted()
    {
        // Arrange — create a run; gate blocks all API calls immediately
        await using var ctx = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("Stopping test run", "{}", CancellationToken.None);

        var gate        = new SemaphoreSlim(0, int.MaxValue); // closed
        var gatedClient = new GatedFakeAnthropicClient(gate);

        await using var host = new OrchestratorTestHost(fixture, gatedClient,
            new OrchestratorOptions
            {
                PollingInterval   = TimeSpan.FromMilliseconds(100),
                MaxConcurrentRuns = 1
            });
        await host.StartAsync();

        // Wait until the run transitions to Running (pipeline has started, gate blocks the first API call)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            await using var checkCtx = fixture.NewContext();
            var r = await checkCtx.Runs.FindAsync(runId);
            if (r?.Status == RunStatus.Running) break;
        }

        // Precondition guard — if the run never reached Running the test would pass spuriously
        await using var guardCtx = fixture.NewContext();
        var guardRun = await guardCtx.Runs.FindAsync(runId);
        Assert.True(guardRun?.Status == RunStatus.Running,
            $"Precondition: expected run to be Running before StopAsync, got {guardRun?.Status}");

        // Act — stop the service; StoppingToken is signalled, gate stays closed so
        // the blocked CompleteAsync receives OperationCanceledException
        await host.StopAsync();

        // Assert
        await using var finalCtx = fixture.NewContext();
        var run = await finalCtx.Runs.FindAsync(runId);
        Assert.NotNull(run);

        // Status must indicate cancellation: Aborted, or Failed with a cancel-related error message
        var isCancelled = run.Status is RunStatus.Aborted ||
                          (run.Status == RunStatus.Failed &&
                           (run.ErrorMessage?.Contains("stopping",  StringComparison.OrdinalIgnoreCase) == true ||
                            run.ErrorMessage?.Contains("cancel",    StringComparison.OrdinalIgnoreCase) == true ||
                            run.ErrorMessage?.Contains("restarted", StringComparison.OrdinalIgnoreCase) == true));
        Assert.True(isCancelled,
            $"Expected Aborted or Failed-with-cancel, got Status={run.Status}, ErrorMessage={run.ErrorMessage}");
    }
}
