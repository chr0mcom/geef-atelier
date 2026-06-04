using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

public interface IRunPersistenceService
{
    Task<Guid> CreateRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
        string? crewTemplateName = null,
        string? crewSnapshotJson = null,
        RunKind kind = RunKind.Standard,
        Guid? parentCompositionRunId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the <c>CrewSnapshot</c> JSON for the run with the given <paramref name="runId"/>.
    /// Used to attach run-local grounding providers after the run record has been created.
    /// Throws <see cref="InvalidOperationException"/> if no run with that ID exists.
    /// </summary>
    Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the run as <c>Failed</c> with the given error message.
    /// No-ops if the run does not exist or is already in a terminal state.
    /// </summary>
    Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new run that resumes a previously aborted or failed run.
    /// Sets <c>ParentRunId</c> and optionally <c>SeedDraftText</c>.
    /// </summary>
    Task<Guid> CreateResumedRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser,
        string? crewTemplateName,
        string? crewSnapshotJson,
        Guid parentRunId,
        string? seedDraftText,
        CancellationToken cancellationToken = default);
}
