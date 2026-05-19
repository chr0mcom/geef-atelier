using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Application.Crew.Finalizers;

/// <summary>Resolves the correct <see cref="IFinalizerExecutor"/> for a given <see cref="FinalizerType"/>.</summary>
public interface IFinalizerExecutorFactory
{
    /// <summary>Returns the executor for <paramref name="type"/>. Throws <see cref="InvalidOperationException"/> if the type is not registered.</summary>
    IFinalizerExecutor GetExecutor(FinalizerType type);
}
