namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>
/// Shadow-mode record of the intake of an order that already runs (<c>OrderIntakeShadows</c>). Kept apart from the order so
/// that the shadow never shows or changes anything.
/// </summary>
public sealed record OrderIntakeShadowEntity
{
    public required Guid OrderId { get; init; }

    /// <summary>One of <see cref="IntakeShadowStatuses"/>.</summary>
    public required string Status { get; init; }

    public int Attempts { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }

    /// <summary>Duration of the clarifier call alone.</summary>
    public int? DurationMs { get; init; }

    /// <summary>Number of questions after <see cref="QuestionGate"/>.</summary>
    public int? QuestionCount { get; init; }

    /// <summary>The complete <see cref="IntakeRecord"/> as JSON.</summary>
    public string? RecordJson { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>Values of <see cref="OrderIntakeShadowEntity.Status"/>.</summary>
public static class IntakeShadowStatuses
{
    public const string Pending = "Pending";
    public const string Done = "Done";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
    public const string SkippedCap = "SkippedCap";
}
