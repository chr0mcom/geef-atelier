using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Core.Persistence;

/// <summary>Persistence access for <see cref="RunArtifact"/> records produced by finalizer steps.</summary>
public interface IRunArtifactRepository
{
    /// <summary>Returns all artifacts for the given run, ordered by creation time.</summary>
    Task<IReadOnlyList<RunArtifact>> ListByRunAsync(Guid runId, CancellationToken ct);

    /// <summary>Returns the artifact with the given id, or <c>null</c> if not found.</summary>
    Task<RunArtifact?> GetByIdAsync(Guid artifactId, CancellationToken ct);

    /// <summary>Persists a new artifact record and returns it.</summary>
    Task<RunArtifact> CreateAsync(RunArtifact artifact, CancellationToken ct);

    /// <summary>Deletes all artifacts for the given run. Used when a run is deleted.</summary>
    Task DeleteByRunAsync(Guid runId, CancellationToken ct);
}
