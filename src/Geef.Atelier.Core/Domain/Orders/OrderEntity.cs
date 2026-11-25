using Geef.Atelier.Core.Domain.Intake;

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

    /// <summary>How the intake ended; null when the order was not clarified (intake off, bypassed, older orders).</summary>
    public IntakeOutcome? IntakeOutcome { get; init; }

    /// <summary>Number of times the intake worker claimed the order.</summary>
    public int IntakeAttempts { get; init; }

    /// <summary>Start of the clarifier call.</summary>
    public DateTimeOffset? IntakeStartedAt { get; init; }

    /// <summary>End of the clarifier call or of its fallback.</summary>
    public DateTimeOffset? IntakeFinishedAt { get; init; }

    /// <summary>When the order entered <see cref="OrderState.NeedsInput"/>; the automatic start counts from here.</summary>
    public DateTimeOffset? QuestionsAskedAt { get; init; }

    /// <summary>When the questions were answered, skipped or the order started on its own.</summary>
    public DateTimeOffset? AnsweredAt { get; init; }

    /// <summary>The research of the order (<c>OrderResearch</c> as JSON), set once by the first run that did research.</summary>
    public string? ResearchJson { get; init; }

    /// <summary>When <see cref="ResearchJson"/> was set; counts toward the daily limit of the research.</summary>
    public DateTimeOffset? ResearchAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
