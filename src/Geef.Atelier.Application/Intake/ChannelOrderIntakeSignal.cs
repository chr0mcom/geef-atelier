using System.Threading.Channels;

namespace Geef.Atelier.Application.Intake;

/// <summary>Wake-up signal of the intake worker: a channel of capacity one, so that many wake-ups between two polls count once.</summary>
internal sealed class ChannelOrderIntakeSignal : IOrderIntakeSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    public void Wake() => _channel.Writer.TryWrite(true);

    public async ValueTask WaitAsync(TimeSpan max, CancellationToken ct)
    {
        if (_channel.Reader.TryRead(out _))
            return;
        if (max <= TimeSpan.Zero)
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(max);
        try
        {
            if (await _channel.Reader.WaitToReadAsync(timeout.Token))
                _channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }
}
