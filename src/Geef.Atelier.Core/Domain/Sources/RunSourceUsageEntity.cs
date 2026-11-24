namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>
/// Records which source went into which prompt of a run and to what extent. <see cref="FileName"/> is kept so the
/// record stays readable after the source is deleted.
/// </summary>
public sealed record RunSourceUsageEntity
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }
    public Guid? SourceId { get; init; }
    public required string FileName { get; init; }
    public required SourceUsageMode Mode { get; init; }
    public required string Target { get; init; }
    public required int CharsIncluded { get; init; }
    public string? PagesIncluded { get; init; }
    public required int Position { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
