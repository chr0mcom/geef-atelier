using System.Reflection;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Stand-in for a service that a test expects never to be used: every call fails the test.</summary>
public class NeverCalled<T> : DispatchProxy where T : class
{
    /// <summary>Creates a stand-in for <typeparamref name="T"/>.</summary>
    public static T Create() => Create<T, NeverCalled<T>>();

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException($"{typeof(T).Name}.{targetMethod?.Name} must not be called.");
}
