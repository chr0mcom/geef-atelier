using Geef.Sdk.Events;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Pipeline;

internal sealed class OutputEventSink(ITestOutputHelper output) : IGeefEventSink
{
    public ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken)
    {
        output.WriteLine($"[{geefEvent.Timestamp:HH:mm:ss.fff}] {geefEvent.GetType().Name} (Run={geefEvent.RunId})");
        return ValueTask.CompletedTask;
    }
}
