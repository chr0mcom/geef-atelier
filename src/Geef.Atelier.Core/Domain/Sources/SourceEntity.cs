namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>
/// A file uploaded by one person: the original lives on the sources volume, the extracted text in the database.
/// <see cref="ExtractedText"/> carries one page marker per PDF page on its own line; no line is longer than the
/// configured maximum.
/// </summary>
public sealed record SourceEntity
{
    public required Guid Id { get; init; }
    public required string OwnerUsername { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required SourceKind Kind { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public string? StoragePath { get; init; }
    public string? ExtractedText { get; init; }
    public int? PageCount { get; init; }
    public required int CharCount { get; init; }
    public required int WordCount { get; init; }
    public required SourceQuality Quality { get; init; }
    public required IReadOnlyList<SourceWarning> Warnings { get; init; }
    public required SourceOrigin Origin { get; init; }
    public bool InLibrary { get; init; }
    public bool AlwaysInclude { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
