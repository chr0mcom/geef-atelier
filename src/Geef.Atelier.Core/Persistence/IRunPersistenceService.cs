namespace Geef.Atelier.Core.Persistence;

public interface IRunPersistenceService
{
    Task<Guid> CreateRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
        string? crewTemplateName = null,
        string? crewSnapshotJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the <c>CrewSnapshot</c> JSON for the run with the given <paramref name="runId"/>.
    /// Used to attach run-local grounding providers after the run record has been created.
    /// </summary>
    Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default);
}
