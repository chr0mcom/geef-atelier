namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>A stored row of <see cref="SourceRefusalRecord"/> with its id and time.</summary>
public sealed record SourceRefusalEntity
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string OwnerUsername { get; init; }
    public required RefusalPath Path { get; init; }
    public required string Code { get; init; }
    public SourceKind? Kind { get; init; }
    public string? Extension { get; init; }
    public string? DeclaredType { get; init; }
    public long? SizeBytes { get; init; }
}
