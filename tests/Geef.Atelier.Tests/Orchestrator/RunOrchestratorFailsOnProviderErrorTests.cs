using System.Net;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Verifies that <c>RunOrchestratorService</c> transitions a run to <see cref="RunStatus.Failed"/>
/// when the LLM provider throws an exception during pipeline execution.
/// Previously the run would remain stuck in <see cref="RunStatus.Running"/> (bug: generic catch only logged).
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorFailsOnProviderErrorTests(PostgresFixture fixture)
{
    private async Task<RunEntity?> WaitForTerminalStatusAsync(Guid runId, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        RunEntity? run = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            await using var ctx = fixture.NewContext();
            run = await ctx.Runs.FindAsync(runId);
            if (run?.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted)
                return run;
        }
        return run;
    }

    [Fact(Timeout = 30_000)]
    public async Task Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_400()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("HTTP 400 test", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, ThrowingLlmClient.HttpError(HttpStatusCode.BadRequest));
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("400", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact(Timeout = 30_000)]
    public async Task Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_401()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("HTTP 401 test", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, ThrowingLlmClient.HttpError(HttpStatusCode.Unauthorized));
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("authentication", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact(Timeout = 30_000)]
    public async Task Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_500()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("HTTP 500 test", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, ThrowingLlmClient.HttpError(HttpStatusCode.InternalServerError));
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("unavailable", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact(Timeout = 30_000)]
    public async Task Run_TransitionsToFailed_WhenLlmClientThrowsTaskCanceledException()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("Timeout test", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, ThrowingLlmClient.Timeout());
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("timed out", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact(Timeout = 30_000)]
    public async Task Run_TransitionsToFailed_WhenLlmClientThrowsGenericException()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("Generic error test", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, ThrowingLlmClient.GenericError("Totally unexpected error."));
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.Contains("Pipeline execution failed", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact(Timeout = 30_000)]
    public async Task SuccessfulRun_StillCompletesNormally()
    {
        await using var ctx   = fixture.NewContext();
        var svc   = new RunPersistenceService(ctx);
        var runId = await svc.CreateRunAsync("Regression: successful run", "{}", cancellationToken: CancellationToken.None);

        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient());
        await host.StartAsync();

        var run = await WaitForTerminalStatusAsync(runId);

        await host.StopAsync();

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.FinalText);
    }
}
