using System.Collections.Concurrent;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Intake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Intake;

public sealed class OrderIntakeWorkerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private sealed class GatedIntakeService : IOrderIntakeService
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Guid> Processed { get; } = new();
        public ConcurrentQueue<Guid> Shadowed { get; } = new();
        public ConcurrentQueue<DateTimeOffset> StuckSweeps { get; } = new();
        public ConcurrentQueue<DateTimeOffset> UnansweredSweeps { get; } = new();
        public int Running;
        public int MaxRunning;

        public async Task<IntakeProcessResult> ProcessAsync(Guid orderId, CancellationToken ct)
        {
            Processed.Enqueue(orderId);
            await Hold(ct);
            return IntakeProcessResult.Started;
        }

        public async Task<IntakeProcessResult> ProcessShadowAsync(Guid orderId, CancellationToken ct)
        {
            Shadowed.Enqueue(orderId);
            await Hold(ct);
            return IntakeProcessResult.ShadowRecorded;
        }

        public Task<int> StartStuckAsync(DateTimeOffset now, CancellationToken ct)
        {
            StuckSweeps.Enqueue(now);
            return Task.FromResult(0);
        }

        public Task<int> AutoStartUnansweredAsync(DateTimeOffset now, CancellationToken ct)
        {
            UnansweredSweeps.Enqueue(now);
            return Task.FromResult(0);
        }

        private async Task Hold(CancellationToken ct)
        {
            var running = Interlocked.Increment(ref Running);
            lock (this)
                MaxRunning = Math.Max(MaxRunning, running);
            try
            {
                await Gate.Task.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref Running);
            }
        }
    }

    private readonly IntakeOrders _orders = new();
    private readonly IntakeShadows _shadows = new();
    private readonly GatedIntakeService _service = new();
    private readonly ManualTime _time = new(Start);
    private readonly ChannelOrderIntakeSignal _signal = new();

    private OrderIntakeWorker Worker(IntakeOptions? options = null, IntakeMode mode = IntakeMode.On)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderRepository>(_orders);
        services.AddSingleton<IIntakeShadowRepository>(_shadows);
        services.AddSingleton<IOrderIntakeService>(_service);
        var provider = services.BuildServiceProvider();
        return new OrderIntakeWorker(provider.GetRequiredService<IServiceScopeFactory>(), _signal,
            Options.Create(new OrderOptions { IntakeMode = mode }), Options.Create(options ?? new IntakeOptions()),
            NullLogger<OrderIntakeWorker>.Instance, _time);
    }

    private static async Task Drain(OrderIntakeWorker worker) => await Task.WhenAll(worker.InFlight);

    [Fact]
    public async Task Candidates_are_claimed_and_processed()
    {
        var a = Guid.NewGuid();
        _orders.Candidates.Add(a);
        var worker = Worker();
        _service.Gate.SetResult();

        await worker.RunOnceAsync(CancellationToken.None);
        await Drain(worker);

        Assert.Equal([a], _service.Processed);
        var claim = Assert.Single(_orders.Claims);
        Assert.Equal((a, Start, 2), (claim.OrderId, claim.Now, claim.MaxAttempts));
    }

    [Fact]
    public async Task Claims_of_a_crashed_process_are_free_at_once_and_own_claims_only_after_the_lease()
    {
        var worker = Worker();

        Assert.Equal(Start, worker.StaleBefore(Start));
        Assert.Equal(Start, worker.StaleBefore(Start.AddSeconds(30)));
        Assert.Equal(Start.AddSeconds(10), worker.StaleBefore(Start.AddSeconds(100)));

        _time.Now = Start.AddSeconds(30);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(Start, Assert.Single(_orders.CandidateQueries).StaleBefore);
    }

    [Fact]
    public async Task A_refused_claim_is_not_processed()
    {
        var a = Guid.NewGuid();
        _orders.Candidates.Add(a);
        _orders.ClaimRefused.Add(a);
        var worker = Worker();

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Empty(_service.Processed);
        Assert.Empty(worker.InFlight);
    }

    [Fact]
    public async Task At_most_max_concurrent_orders_run_at_the_same_time()
    {
        _orders.Candidates.AddRange(Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()));
        var worker = Worker(new IntakeOptions { MaxConcurrent = 2 });

        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        await WaitUntil(() => _service.Running == 2);

        Assert.Equal(2, worker.InFlight.Count);
        Assert.Equal(3, _orders.Candidates.Count);

        _service.Gate.SetResult();
        await Drain(worker);
        await worker.RunOnceAsync(CancellationToken.None);
        await Drain(worker);
        await worker.RunOnceAsync(CancellationToken.None);
        await Drain(worker);

        Assert.Equal(5, _service.Processed.Count);
        Assert.Equal(2, _service.MaxRunning);
    }

    [Fact]
    public async Task Sweeps_run_once_a_minute()
    {
        var worker = Worker();

        await worker.RunOnceAsync(CancellationToken.None);
        _time.Now = Start.AddSeconds(30);
        await worker.RunOnceAsync(CancellationToken.None);
        _time.Now = Start.AddSeconds(61);
        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal([Start, Start.AddSeconds(61)], _service.StuckSweeps);
        Assert.Equal([Start, Start.AddSeconds(61)], _service.UnansweredSweeps);
    }

    [Fact]
    public async Task Shadow_rows_are_worked_only_in_shadow_mode_and_with_an_empty_live_queue()
    {
        var shadow = Guid.NewGuid();
        _shadows.Pending.Add(shadow);
        _orders.Candidates.Add(Guid.NewGuid());
        _orders.ClaimRefused.UnionWith(_orders.Candidates);
        var worker = Worker(mode: IntakeMode.Shadow);
        _service.Gate.SetResult();

        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Empty(_service.Shadowed);
        Assert.Equal(0, _shadows.Lists);

        _orders.Candidates.Clear();
        await worker.RunOnceAsync(CancellationToken.None);
        await Drain(worker);
        Assert.Equal([shadow], _service.Shadowed);
    }

    [Theory]
    [InlineData(IntakeMode.On)]
    [InlineData(IntakeMode.Off)]
    public async Task Shadow_rows_are_ignored_outside_shadow_mode(IntakeMode mode)
    {
        _shadows.Pending.Add(Guid.NewGuid());
        var worker = Worker(mode: mode);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Empty(_service.Shadowed);
        Assert.Equal(0, _shadows.Lists);
    }

    [Fact]
    public async Task Live_orders_are_worked_even_when_the_intake_is_off()
    {
        var leftover = Guid.NewGuid();
        _orders.Candidates.Add(leftover);
        var worker = Worker(mode: IntakeMode.Off);
        _service.Gate.SetResult();

        await worker.RunOnceAsync(CancellationToken.None);
        await Drain(worker);

        Assert.Equal([leftover], _service.Processed);
    }

    [Fact]
    public async Task The_worker_loop_runs_and_stops_cleanly()
    {
        var a = Guid.NewGuid();
        _orders.Candidates.Add(a);
        _service.Gate.SetResult();
        var worker = Worker(new IntakeOptions { PollInterval = TimeSpan.FromMilliseconds(50) });

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => _service.Processed.Contains(a));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal([a], _service.Processed);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached in time.");
            await Task.Delay(10);
        }
    }
}
