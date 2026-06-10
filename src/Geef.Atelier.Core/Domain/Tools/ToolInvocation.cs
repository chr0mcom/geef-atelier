namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>
/// Records a single tool call made by an LLM actor during a run.
/// </summary>
/// <remarks>
/// Immutable audit entry — never updated after creation.
/// <c>OutputExcerpt</c> is intentionally capped at 500 characters and must never contain secrets.
/// </remarks>
public sealed class ToolInvocation
{
    /// <summary>Unique identifier for this invocation record.</summary>
    public Guid Id { get; init; }

    /// <summary>The run during which this tool was invoked.</summary>
    public Guid RunId { get; init; }

    /// <summary>Pipeline iteration in which the invocation occurred (0-based).</summary>
    public int IterationNumber { get; init; }

    /// <summary>
    /// Actor role that invoked the tool.
    /// Known values: <c>executor</c>, <c>reviewer</c>, <c>advisor</c>, <c>finalizer</c>.
    /// </summary>
    public string ActorType { get; init; } = "";

    /// <summary>Profile name of the actor that invoked the tool (e.g. <c>devils-advocate</c>).</summary>
    public string ActorName { get; init; } = "";

    /// <summary>Tool name as registered in the tool catalogue (kebab-case).</summary>
    public string ToolName { get; init; } = "";

    /// <summary>Discriminator matching <see cref="ToolType"/> constants.</summary>
    public string ToolType { get; init; } = "";

    /// <summary>Full JSON input object supplied by the LLM for this invocation.</summary>
    public string InputJson { get; init; } = "{}";

    /// <summary>
    /// Truncated excerpt of the tool output (max 500 characters).
    /// <c>null</c> when the tool produced no output or when recording was suppressed to protect secrets.
    /// </summary>
    public string? OutputExcerpt { get; init; }

    /// <summary>Estimated cost of this tool call in EUR, or <c>null</c> when unknown.</summary>
    public decimal? CostEur { get; init; }

    /// <summary>Wall-clock duration of the tool call in milliseconds.</summary>
    public int DurationMs { get; init; }

    /// <summary>Monotonic sequence number within the run — used to order invocations chronologically.</summary>
    public int Sequence { get; init; }

    /// <summary>Outcome of the invocation.</summary>
    public ToolInvocationOutcome Outcome { get; init; }

    /// <summary>UTC timestamp at which the invocation was recorded.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
