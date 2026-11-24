namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>Links a source to an order, with its role, its position in the source block and an optional page selection.</summary>
public sealed record OrderSourceEntity
{
    public required Guid OrderId { get; init; }
    public required Guid SourceId { get; init; }
    public required SourceRole Role { get; init; }
    public required int Position { get; init; }
    public string? PageSelection { get; init; }
    public required DateTimeOffset AddedAt { get; init; }
}
