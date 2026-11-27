using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

public interface IRunPersistenceService
{
    Task<Guid> CreateRunAsync(
        string briefingText,
        string? createdByUser = null,
        string? crewTemplateName = null,
        string? crewSnapshotJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the <c>CrewSnapshot</c> JSON for the run with the given <paramref name="runId"/>.
    /// A run without a snapshot is not picked up for processing; saving the snapshot releases the run.
    /// Throws <see cref="InvalidOperationException"/> if no run with that ID exists.
    /// </summary>
    Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the run as <c>Failed</c> with the given error message.
    /// No-ops if the run does not exist or is already in a terminal state.
    /// </summary>
    Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default);
}
