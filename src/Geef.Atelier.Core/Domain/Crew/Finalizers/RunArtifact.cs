namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>Represents an artifact produced by a finalizer step for a specific run.</summary>
public sealed class RunArtifact
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }

    /// <summary>Name of the <see cref="FinalizerProfile"/> that produced this artifact.</summary>
    public required string FinalizerProfileName { get; init; }

    public required ArtifactType ArtifactType { get; init; }

    /// <summary>Original filename for <see cref="ArtifactType.File"/> artifacts; null otherwise.</summary>
    public string? Filename { get; init; }

    /// <summary>MIME content type for <see cref="ArtifactType.File"/> artifacts; null otherwise.</summary>
    public string? ContentType { get; init; }

    /// <summary>File size in bytes for <see cref="ArtifactType.File"/> artifacts; null otherwise.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// For <see cref="ArtifactType.File"/>: absolute path on the export volume.
    /// For <see cref="ArtifactType.Url"/>: the URL returned by the external sink.
    /// For <see cref="ArtifactType.Status"/>: a short status string.
    /// </summary>
    public required string StorageUri { get; init; }

    /// <summary>Human-readable note for <see cref="ArtifactType.Status"/> artifacts; null for file/URL artifacts.</summary>
    public string? StatusMessage { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
