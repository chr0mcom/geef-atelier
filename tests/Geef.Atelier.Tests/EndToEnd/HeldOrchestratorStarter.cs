using Geef.Atelier.Web.Services;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>Opens once; lets the held orchestrator start polling.</summary>
internal sealed class OrchestratorGate
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when <see cref="Release"/> has been called.</summary>
    public Task WaitAsync(CancellationToken ct) => _released.Task.WaitAsync(ct);

    /// <summary>Allows the orchestrator to start.</summary>
    public void Release() => _released.TrySetResult();
}

/// <summary>
/// Starts the real <see cref="RunOrchestratorService"/> only after the test released the gate. The
/// submission path still has the known race (the run is <c>Pending</c> before its attachments are indexed
/// and its snapshot is patched, report A finding F3); holding the poller until the submission returned keeps
/// that race out of tests that check prompt wiring.
/// </summary>
internal sealed class HeldOrchestratorStarter(RunOrchestratorService orchestrator, OrchestratorGate gate) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _startTask;
    private volatile bool _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startTask = Task.Run(async () =>
        {
            await gate.WaitAsync(_stopping.Token);
            await orchestrator.StartAsync(_stopping.Token);
            _started = true;
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_startTask is not null)
        {
            try
            {
                await _startTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_started)
            await orchestrator.StopAsync(cancellationToken);
    }
}
