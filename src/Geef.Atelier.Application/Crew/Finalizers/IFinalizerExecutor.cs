using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Application.Crew.Finalizers;

/// <summary>
/// Executes a single finalizer step for a given <see cref="FinalizerProfile"/>.
/// Implementations live in the Infrastructure layer; one implementation per <see cref="FinalizerType"/>.
/// </summary>
public interface IFinalizerExecutor
{
    /// <summary>The finalizer type this executor handles.</summary>
    FinalizerType Type { get; }

    /// <summary>
    /// Executes the finalizer step and returns the result.
    /// Implementations must be tolerant of failures: catch and log internally, then return a
    /// <see cref="FinalizerExecutionResult"/> with a <see cref="ArtifactType.Status"/> artifact
    /// describing the error. Do NOT re-throw — partial success is the contract. The one exception is
    /// cancellation: an <see cref="OperationCanceledException"/> caused by <paramref name="cancellationToken"/> (user
    /// cancel or service stop) must propagate, because it is not a failure of the step. The status message is a short
    /// plain-English sentence for the user; technical details belong in the log only.
    /// </summary>
    Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken);
}
