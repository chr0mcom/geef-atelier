using System.Collections.Concurrent;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>
/// Works the intake queue (contract stage 2, 4.1): claims orders in <c>Analyzing</c> atomically and clarifies at most
/// <see cref="IntakeOptions.MaxConcurrent"/> at a time, starts stuck and unanswered orders once a minute, and in shadow mode
/// clarifies shadow rows only while the live queue is empty. A claim made before this process started counts as abandoned
/// at once; other claims after <see cref="IntakeOptions.ClaimLease"/>.
/// </summary>
internal sealed class OrderIntakeWorker(
    IServiceScopeFactory scopes,
    IOrderIntakeSignal signal,
    IOptions<OrderOptions> orderOptions,
    IOptions<IntakeOptions> options,
    ILogger<OrderIntakeWorker> logger,
    TimeProvider time) : BackgroundService
{
    /// <summary>Interval of the sweeps for stuck and unanswered orders.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly DateTimeOffset _processStart = time.GetUtcNow();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxConcurrent));
    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();
    private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

    /// <summary>Work started by the last passes that has not finished yet.</summary>
    internal IReadOnlyCollection<Task> InFlight => _inFlight.Values.ToList();

    /// <summary>
    /// Claims older than this are free: the later of "now minus the lease" and the start of this process, so that claims of a
    /// crashed process are taken over at once.
    /// </summary>
    internal DateTimeOffset StaleBefore(DateTimeOffset now)
    {
        var leased = now - options.Value.ClaimLease;
        return leased > _processStart ? leased : _processStart;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Intake worker pass failed: {Error}", ex.ToRedactedLogText());
            }

            try
            {
                await signal.WaitAsync(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var running = _inFlight.Values.ToArray();
        try
        {
            await Task.WhenAll(running);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One pass: live queue, then the sweeps when due, then shadow rows when the live queue is empty.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var staleBefore = StaleBefore(now);

        var liveWaiting = await StartLiveAsync(settings, now, staleBefore, ct);

        if (now >= _nextSweep)
        {
            _nextSweep = now + SweepInterval;
            await SweepAsync(now, ct);
        }

        if (!liveWaiting && orderOptions.Value.IntakeMode == IntakeMode.Shadow)
            await StartShadowAsync(settings, now, staleBefore, ct);
    }

    private async Task<bool> StartLiveAsync(IntakeOptions settings, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var free = _slots.CurrentCount;
        var candidates = await orders.ListIntakeCandidatesAsync(staleBefore, settings.MaxAttempts, Math.Max(1, free), ct);
        foreach (var orderId in candidates)
        {
            if (_inFlight.ContainsKey(orderId) || !await _slots.WaitAsync(0, ct))
                continue;
            await ClaimAndLaunchAsync(
                () => orders.TryClaimForIntakeAsync(orderId, now, staleBefore, settings.MaxAttempts, ct),
                orderId, (service, token) => service.ProcessAsync(orderId, token), ct);
        }
        return candidates.Count > 0;
    }

    private async Task StartShadowAsync(IntakeOptions settings, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken ct)
    {
        if (_slots.CurrentCount == 0)
            return;
        await using var scope = scopes.CreateAsyncScope();
        var shadows = scope.ServiceProvider.GetRequiredService<IIntakeShadowRepository>();
        foreach (var orderId in await shadows.ListPendingAsync(staleBefore, settings.MaxAttempts, _slots.CurrentCount, ct))
        {
            if (_inFlight.ContainsKey(orderId) || !await _slots.WaitAsync(0, ct))
                continue;
            await ClaimAndLaunchAsync(
                () => shadows.TryClaimAsync(orderId, now, staleBefore, settings.MaxAttempts, ct),
                orderId, (service, token) => service.ProcessShadowAsync(orderId, token), ct);
        }
    }

    private async Task SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrderIntakeService>();
        try
        {
            var stuck = await service.StartStuckAsync(now, ct);
            if (stuck > 0)
                logger.LogWarning("Intake worker started {Count} stuck orders without questions.", stuck);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Intake sweep for stuck orders failed: {Error}", ex.ToRedactedLogText());
        }

        try
        {
            var unanswered = await service.AutoStartUnansweredAsync(now, ct);
            if (unanswered > 0)
                logger.LogInformation("Intake worker started {Count} unanswered orders with their assumptions.", unanswered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Intake sweep for unanswered orders failed: {Error}", ex.ToRedactedLogText());
        }
    }

    /// <summary>Claims with the slot already taken; the slot goes back unless the work was launched, also when the claim throws.</summary>
    private async Task ClaimAndLaunchAsync(
        Func<Task<bool>> claim, Guid orderId, Func<IOrderIntakeService, CancellationToken, Task<IntakeProcessResult>> work, CancellationToken ct)
    {
        var launched = false;
        try
        {
            if (!await claim())
                return;
            Launch(orderId, work, ct);
            launched = true;
        }
        finally
        {
            if (!launched)
                _slots.Release();
        }
    }

    private void Launch(Guid orderId, Func<IOrderIntakeService, CancellationToken, Task<IntakeProcessResult>> work, CancellationToken ct)
    {
        var runner = new Task<Task>(() => RunAsync(orderId, work, ct), CancellationToken.None);
        _inFlight[orderId] = runner.Unwrap();
        runner.Start(TaskScheduler.Default);
    }

    private async Task RunAsync(Guid orderId, Func<IOrderIntakeService, CancellationToken, Task<IntakeProcessResult>> work, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await work(scope.ServiceProvider.GetRequiredService<IOrderIntakeService>(), ct);
            logger.LogInformation("Intake of order {OrderId}: {Result}.", orderId, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Intake of order {OrderId} failed: {Error}", orderId, ex.ToRedactedLogText());
        }
        finally
        {
            _inFlight.TryRemove(orderId, out _);
            _slots.Release();
            signal.Wake();
        }
    }

    public override void Dispose()
    {
        _slots.Dispose();
        base.Dispose();
    }
}
