using System.Collections.Concurrent;
using Geef.Sdk.Events;

namespace Geef.Atelier.Tests.Pipeline;

internal sealed class CountingEventSink : IGeefEventSink
{
    public ConcurrentDictionary<Type, int> Counts { get; } = new();

    public ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken)
    {
        Counts.AddOrUpdate(geefEvent.GetType(), 1, (_, v) => v + 1);
        return ValueTask.CompletedTask;
    }

    public int Get<T>() where T : IGeefEvent =>
        Counts.TryGetValue(typeof(T), out var count) ? count : 0;
}
