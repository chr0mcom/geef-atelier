using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Application.Tools;

/// <summary>Input context for a tool invocation — used for audit tracing and run correlation.</summary>
/// <param name="RunId">The run in which the tool is being invoked.</param>
/// <param name="IterationNumber">0-based pipeline iteration index.</param>
/// <param name="ActorType">Role of the invoking actor (e.g. <c>executor</c>, <c>reviewer</c>).</param>
/// <param name="ActorName">Profile name of the invoking actor (e.g. <c>devils-advocate</c>).</param>
/// <param name="Sequence">Monotonic sequence number within the run for chronological ordering.</param>
public sealed record ToolInvocationContext(
    Guid RunId,
    int IterationNumber,
    string ActorType,
    string ActorName,
    int Sequence);

/// <summary>Result of executing a tool.</summary>
/// <param name="Output">Text output produced by the tool; empty string on failure.</param>
/// <param name="CostEur">Estimated cost of the tool call in EUR, or <see langword="null"/> when unknown.</param>
/// <param name="Error">
/// Error message when execution failed; <see langword="null"/> on success.
/// Never contains secrets.
/// </param>
public sealed record ToolExecutionResult(
    string Output,
    decimal? CostEur,
    string? Error);

/// <summary>
/// Executes a <see cref="ToolDefinition"/> and persists the resulting <c>ToolInvocation</c> audit record.
/// This is the unified execution layer for agentic (Pull) tool calls made during actor turns.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes the tool described by <paramref name="tool"/> with the given <paramref name="inputJson"/>
    /// and records the invocation for audit purposes.
    /// </summary>
    /// <param name="tool">The tool definition to execute.</param>
    /// <param name="inputJson">JSON string of the input object supplied by the LLM.</param>
    /// <param name="ctx">Audit context identifying the run, iteration, and invoking actor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The execution result including output, cost, and any error description.</returns>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default);
}
