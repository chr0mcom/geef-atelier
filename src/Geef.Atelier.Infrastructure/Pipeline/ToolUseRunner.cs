using System.Text.Json;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Drives an agentic tool-use loop: each turn the LLM may call tools, whose results are fed
/// back as <c>tool</c> messages; the loop ends when the model stops calling tools, a required
/// final tool is called, the cap is reached, or the caller cancels.
/// </summary>
internal sealed class ToolUseRunner(
    IToolExecutor toolExecutor,
    IToolSchemaProvider schemaProvider,
    ICostAccumulator? costAccumulator = null,
    ILogger<ToolUseRunner>? logger = null) : IToolUseRunner
{
    /// <inheritdoc/>
    public async Task<ToolLoopResult> RunAsync(
        ILlmClient client,
        string model,
        string systemPrompt,
        string initialUserPrompt,
        IReadOnlyList<ToolDefinition> boundTools,
        string? requiredFinalTool,
        ToolLoopOptions options,
        ToolInvocationContext invocationContext,
        CancellationToken ct = default)
    {
        // Build LlmTool list from the bound tool definitions.
        var llmTools = BuildLlmTools(boundTools);

        // Seed the conversation history with the system and user turns.
        var history = new List<LlmMessage>
        {
            LlmMessage.System(systemPrompt),
            LlmMessage.User(initialUserPrompt)
        };

        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var toolCallCount = 0;
        var sequenceOffset = invocationContext.Sequence;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // --- LLM turn ---
            var request = new LlmRequest
            {
                Model      = model,
                SystemPrompt = systemPrompt,   // kept for non-loop callers; loop uses Messages
                UserPrompt   = initialUserPrompt,
                Messages     = history,
                Tools        = llmTools,
                ToolChoice   = "auto"
            };

            LlmResponse response;
            try
            {
                response = await client.CompleteAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ToolLoopResult
                {
                    FinalText      = "",
                    EndReason      = ToolLoopEndReason.Cancelled,
                    ToolCallCount  = toolCallCount,
                    TotalTokenUsage = new LlmTokenUsage
                    {
                        InputTokens  = totalInputTokens,
                        OutputTokens = totalOutputTokens
                    }
                };
            }

            totalInputTokens  += response.TokenUsage.InputTokens;
            totalOutputTokens += response.TokenUsage.OutputTokens;

            // Accumulate LLM cost when a tracker is wired in.
            if (costAccumulator is not null)
            {
                costAccumulator.RecordActorCost(
                    invocationContext.IterationNumber,
                    ActorType.Executor,
                    invocationContext.ActorName,
                    model,
                    response.TokenUsage.InputTokens,
                    response.TokenUsage.OutputTokens,
                    costEur: null);
            }

            var calls = response.AllToolCalls;

            // --- Required final tool check ---
            if (requiredFinalTool is not null)
            {
                var finalCall = calls.FirstOrDefault(c =>
                    string.Equals(c.Name, requiredFinalTool, StringComparison.OrdinalIgnoreCase));
                if (finalCall is not null)
                {
                    return new ToolLoopResult
                    {
                        FinalText      = finalCall.ArgumentsJson,
                        EndReason      = ToolLoopEndReason.RequiredToolCalled,
                        ToolCallCount  = toolCallCount,
                        TotalTokenUsage = new LlmTokenUsage
                        {
                            InputTokens  = totalInputTokens,
                            OutputTokens = totalOutputTokens
                        }
                    };
                }
            }

            // --- No tool calls → model returned final text ---
            if (calls.Count == 0 || response.FinishReason == "stop")
            {
                return new ToolLoopResult
                {
                    FinalText      = response.Text,
                    EndReason      = ToolLoopEndReason.FinalText,
                    ToolCallCount  = toolCallCount,
                    TotalTokenUsage = new LlmTokenUsage
                    {
                        InputTokens  = totalInputTokens,
                        OutputTokens = totalOutputTokens
                    }
                };
            }

            // --- Tool execution round ---

            // Append the assistant message that contains the tool calls.
            history.Add(LlmMessage.AssistantToolCalls(calls));

            foreach (var call in calls)
            {
                toolCallCount++;
                var seq = sequenceOffset + toolCallCount;

                // Find the matching ToolDefinition by name.
                var toolDef = boundTools.FirstOrDefault(t =>
                    string.Equals(t.Name, call.Name, StringComparison.OrdinalIgnoreCase));

                string toolResultContent;

                if (toolDef is null)
                {
                    toolResultContent = $"Error: unknown tool '{call.Name}'.";
                    logger?.LogWarning(
                        "ToolUseRunner: unknown tool called: name={ToolName} run={RunId}",
                        call.Name, invocationContext.RunId);
                }
                else
                {
                    var toolCtx = invocationContext with { Sequence = seq };

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(options.PerToolTimeout);

                    ToolExecutionResult toolResult;
                    try
                    {
                        toolResult = await toolExecutor.ExecuteAsync(
                            toolDef, call.ArgumentsJson, toolCtx, timeoutCts.Token);
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        // Per-tool timeout fired; outer cancellation is still active.
                        logger?.LogWarning(ex,
                            "ToolUseRunner: tool timed out: name={ToolName} run={RunId}",
                            call.Name, invocationContext.RunId);
                        toolResult = new ToolExecutionResult(
                            "", null, $"Tool '{call.Name}' timed out after {options.PerToolTimeout.TotalSeconds:0}s.");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger?.LogError(ex,
                            "ToolUseRunner: tool execution failed: name={ToolName} run={RunId}",
                            call.Name, invocationContext.RunId);
                        toolResult = new ToolExecutionResult("", null, ex.Message);
                    }

                    toolResultContent = toolResult.Error is not null
                        ? $"Error: {toolResult.Error}"
                        : toolResult.Output;
                }

                history.Add(LlmMessage.ToolResult(call.Id, call.Name, toolResultContent));
            }

            // --- Cap check (after executing tools) ---
            if (toolCallCount >= options.MaxToolCalls)
            {
                return new ToolLoopResult
                {
                    FinalText      = $"Tool call cap of {options.MaxToolCalls} reached.",
                    EndReason      = ToolLoopEndReason.CapReached,
                    ToolCallCount  = toolCallCount,
                    TotalTokenUsage = new LlmTokenUsage
                    {
                        InputTokens  = totalInputTokens,
                        OutputTokens = totalOutputTokens
                    }
                };
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IReadOnlyList<LlmTool> BuildLlmTools(IReadOnlyList<ToolDefinition> tools)
    {
        var result = new List<LlmTool>(tools.Count);
        foreach (var tool in tools)
        {
            var schema = schemaProvider.GetSchema(tool);
            result.Add(new LlmTool
            {
                Name        = schema.Name,
                Description = schema.Description,
                InputSchema = JsonDocument.Parse(schema.InputSchemaJson).RootElement
            });
        }
        return result;
    }
}
