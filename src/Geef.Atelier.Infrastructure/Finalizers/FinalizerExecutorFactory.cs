using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class FinalizerExecutorFactory(
    IEnumerable<IFinalizerExecutor> executors) : IFinalizerExecutorFactory
{
    private readonly IReadOnlyDictionary<FinalizerType, IFinalizerExecutor> _executors =
        executors.ToDictionary(e => e.Type);

    public IFinalizerExecutor GetExecutor(FinalizerType type)
    {
        if (_executors.TryGetValue(type, out var executor))
            return executor;
        throw new InvalidOperationException(
            $"No finalizer executor is registered for type '{type}'. " +
            $"Registered types: {string.Join(", ", _executors.Keys)}");
    }
}
