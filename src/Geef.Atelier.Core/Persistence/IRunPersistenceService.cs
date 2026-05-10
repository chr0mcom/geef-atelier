namespace Geef.Atelier.Core.Persistence;

public interface IRunPersistenceService
{
    Task<Guid> CreateRunAsync(string briefingText, string configJson, CancellationToken cancellationToken = default);
}
