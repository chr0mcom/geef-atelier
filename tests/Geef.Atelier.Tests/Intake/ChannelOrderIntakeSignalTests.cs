using Geef.Atelier.Application.Intake;

namespace Geef.Atelier.Tests.Intake;

public sealed class ChannelOrderIntakeSignalTests
{
    [Fact]
    public async Task A_wake_ends_the_wait_at_once()
    {
        var signal = new ChannelOrderIntakeSignal();
        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).AsTask();

        signal.Wake();

        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task Many_wakes_between_two_waits_count_once()
    {
        var signal = new ChannelOrderIntakeSignal();
        signal.Wake();
        signal.Wake();
        signal.Wake();

        await signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);

        Assert.True(watch.ElapsedMilliseconds >= 100);
    }

    [Fact]
    public async Task Without_a_wake_the_wait_ends_after_the_maximum()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await new ChannelOrderIntakeSignal().WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.InRange(watch.ElapsedMilliseconds, 80, 5_000);
    }

    [Fact]
    public async Task Cancelling_the_caller_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChannelOrderIntakeSignal().WaitAsync(TimeSpan.FromSeconds(30), cts.Token).AsTask());
    }
}
