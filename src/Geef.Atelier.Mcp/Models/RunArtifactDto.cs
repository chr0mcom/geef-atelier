namespace Geef.Atelier.Mcp.Models;

public sealed record RunArtifactDto(
    string ArtifactId,
    string RunId,
    string FinalizerProfileName,
    string ArtifactType,
    string? Filename,
    string? ContentType,
    long? SizeBytes,
    string? StorageUri,
    string? StatusMessage,
    DateTimeOffset CreatedAt);
