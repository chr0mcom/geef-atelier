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
}
