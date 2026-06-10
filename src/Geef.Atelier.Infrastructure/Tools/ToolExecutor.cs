using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Tools;

/// <summary>
/// Dispatches tool invocations to the appropriate executor and persists an audit record for
/// each call.  Currently, only <see cref="ToolType.StaticContext"/> is fully wired; all other
/// types return a "not yet implemented" error result (stub behaviour, to be replaced in A-T9).
/// </summary>
internal sealed class ToolExecutor(
    IToolInvocationRepository invocationRepository,
    ILogger<ToolExecutor> logger) : IToolExecutor
{
    /// <inheritdoc/>
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ToolExecutionResult result;

        try
        {
            result = tool.ToolType switch
            {
                ToolType.StaticContext => ExecuteStaticContext(tool),
                _ => new ToolExecutionResult(
                    $"Tool type '{tool.ToolType}' execution not yet implemented.",
                    null,
                    "NotImplemented")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Tool execution failed: tool={ToolName} type={ToolType} run={RunId}",
                tool.Name, tool.ToolType, ctx.RunId);
            result = new ToolExecutionResult("", null, ex.Message);
        }

        sw.Stop();

        var outcome = result.Error is null
            ? ToolInvocationOutcome.Success
            : ToolInvocationOutcome.Failed;

        var invocation = new ToolInvocation
        {
            Id = Guid.NewGuid(),
            RunId = ctx.RunId,
            IterationNumber = ctx.IterationNumber,
            ActorType = ctx.ActorType,
            ActorName = ctx.ActorName,
            ToolName = tool.Name,
            ToolType = tool.ToolType,
            InputJson = inputJson,
            OutputExcerpt = result.Output.Length > 500
                ? result.Output[..500]
                : result.Output,
            CostEur = result.CostEur,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Sequence = ctx.Sequence,
            Outcome = outcome,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await invocationRepository.AddAsync(invocation, ct);

        return result;
    }

    // -------------------------------------------------------------------------
    // Concrete executors
    // -------------------------------------------------------------------------

    private static ToolExecutionResult ExecuteStaticContext(ToolDefinition tool)
    {
        var content = tool.Settings.TryGetValue(ToolDefinitionSettingsKeys.StaticContent, out var v)
            ? v
            : "";
        return new ToolExecutionResult(content, null, null);
    }

    // -------------------------------------------------------------------------
    // Helpers for future concrete executors (A-T9)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the secret value referenced by <see cref="ToolDefinition.SecretRef"/> from
    /// the process environment.  Returns <see langword="null"/> when no reference is set or
    /// the variable is not present.
    /// </summary>
    /// <remarks>
    /// Not called by any current stub executor.  Provided here so A-T9 implementors can
    /// access it without re-discovering the pattern.
    /// </remarks>
    private static string? ResolveSecret(ToolDefinition tool) =>
        tool.SecretRef is { Length: > 0 } secretRef
            ? Environment.GetEnvironmentVariable(secretRef)
            : null;
}
