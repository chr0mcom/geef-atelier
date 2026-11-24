using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Options monitor that always returns the same value and never raises change notifications.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
