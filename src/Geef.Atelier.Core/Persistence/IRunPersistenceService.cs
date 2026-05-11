namespace Geef.Atelier.Core.Persistence;

public interface IRunPersistenceService
{
    Task<Guid> CreateRunAsync(string briefingText, string configJson, string? createdByUser = null, CancellationToken cancellationToken = default);
}
