namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>
/// An order: the unit the person sees. Holds the request in the person's own words; runs underneath stay the log of
/// how the text was made.
/// </summary>
public sealed record OrderEntity
{
    public required Guid Id { get; init; }
    public string? CreatedByUser { get; init; }
    public required string RequestText { get; init; }
    public required OrderMode Mode { get; init; }
    public required OrderState State { get; init; }
    public required OrderEntry Entry { get; init; }
    public string? CrewTemplateName { get; init; }
    public string? ClarifyJson { get; init; }
    public string? AnswersJson { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
