namespace Geef.Atelier.Core.Domain;

/// <summary>Structured event emitted by the Geef pipeline event sink, persisted for audit and live-streaming.</summary>
public sealed record EventEntity
{
    public required long Id { get; init; }
    public required Guid RunId { get; init; }
    public required string EventType { get; init; }

    /// <summary>JSON payload specific to the event type.</summary>
    public required string PayloadJson { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
