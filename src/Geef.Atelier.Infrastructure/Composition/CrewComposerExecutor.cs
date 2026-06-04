using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Execution step that composes a Crew Specification by forcing the LLM to call the
/// <c>submit_crew_spec</c> tool. The resulting tool-call arguments JSON is the pipeline artifact.
/// </summary>
/// <remarks>
/// <para>
/// This executor is the analog of <c>ProfileBasedExecutor</c> but for the Auto-Crew composition
/// pipeline. Instead of generating free-form text, it forces a structured tool call so the output
/// can be deterministically parsed and validated by the downstream reviewer step.
/// </para>
/// <para>
/// The <see cref="Core.Domain.Crew.Profiles.ExecutorProfile"/> is read from the run context via
/// <see cref="AtelierContextKeys.CompositionExecutorProfile"/>; the composition pipeline factory
/// sets this key before the pipeline runs.
/// </para>
/// </remarks>
internal sealed class CrewComposerExecutor(
    ILlmClientResolver llmClientResolver,
    ILogger<CrewComposerExecutor> logger,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null) : IExecutionStep
{
    private const string SystemPromptTemplate = """
        You are an expert crew composer for the Geef.Atelier system. Your task is to design a complete Crew configuration for the user's task.

        {grounded_context}

        IMPORTANT RULES:
        - Always call submit_crew_spec with a complete crew configuration
        - For mode "existing-template": set existing_template_name and leave other fields empty
        - For mode "composed" or "new": specify executor, at least one reviewer, and at least one finalizer
        - Every new reviewer prompt MUST include the severity taxonomy block (critical/major/minor/info)
        - Prefer reusing existing profiles when they fit. New profiles must have complete, task-specific system_prompts
        - Model plurality: reviewer models must differ from the executor model
        """;

    /// <inheritdoc />
    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var profile = context.GetRequired(AtelierContextKeys.CompositionExecutorProfile);
        var brief   = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter    = context.GetRequired(GeefKeys.CurrentIteration);

        // Build the grounded context block (crew catalog + design rules injected by grounding step).
        var groundedContext = string.Empty;
        if (context.TryGet(AtelierContextKeys.GroundingContext, out var groundingCtx) && groundingCtx is not null)
            groundedContext = groundingCtx;

        var systemPrompt = SystemPromptTemplate.Replace("{grounded_context}", groundedContext);

        // On iteration 2+ the reviewer has produced findings; include them so the LLM can revise.
        var userPrompt = BuildUserPrompt(context, brief, iter);

        var (client, model, maxTokens) = llmClientResolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        logger.LogInformation(
            "CrewComposerExecutor: calling {Provider}/{Model} iter={Iter} maxTokens={MaxTokens}",
            profile.Provider, model, iter, maxTokens);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = systemPrompt,
            UserPrompt   = userPrompt,
            MaxTokens    = maxTokens,
            Tools        = [CrewSpecTool.Schema],
            ToolChoice   = $"function:{CrewSpecTool.ToolName}"
        }, cancellationToken);

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider);
            costAccumulator.RecordActorCost(
                iter, ActorType.Executor, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider);
        }

        string artifact;
        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
        {
            // LLM did not honour the forced tool call — produce a structured error artifact so the
            // reviewer can detect and report it rather than silently passing empty output.
            logger.LogWarning(
                "CrewComposerExecutor: LLM did not call {Tool} (finish_reason='{FinishReason}'). Producing error artifact.",
                CrewSpecTool.ToolName, response.FinishReason);

            artifact = $"{{\"_error\": \"LLM did not call {CrewSpecTool.ToolName} (finish_reason='{response.FinishReason}'). Raw response: {EscapeForJson(response.Text)}\"}}";
        }
        else
        {
            logger.LogInformation(
                "CrewComposerExecutor: tool call received, tokens_in={In} tokens_out={Out}",
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens);

            artifact = response.ToolArgumentsJson;
        }

        var updated = context
            .Set(AtelierContextKeys.CurrentDraft, artifact)
            .Set(AtelierContextKeys.TokenUsage, response.TokenUsage);

        return new ExecutionResult
        {
            UpdatedContext = updated,
            Notes =
            [
                $"tokens_in={response.TokenUsage.InputTokens} tokens_out={response.TokenUsage.OutputTokens}",
                $"tool_called={response.FinishReason == "tool_calls"}"
            ]
        };
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static string BuildUserPrompt(IRunContext context, string brief, int iter)
    {
        if (iter == 1)
            return $"Task description:\n{brief}\n\nDesign the optimal crew configuration for this task and call {CrewSpecTool.ToolName}.";

        // Revision pass: include previous reviewer findings so the LLM knows what to fix.
        var findings = ExtractPreviousFindings(context);
        if (findings.Count == 0)
        {
            return $"""
                Task description:
                {brief}

                Your previous crew specification had no findings but did not converge. Revisit the
                configuration and call {CrewSpecTool.ToolName} with an improved crew design.
                """;
        }

        var findingLines = findings
            .Select((f, i) => $"{i + 1}. [{f.Severity.ToString().ToUpperInvariant()}] [{f.ReviewerName}] {f.Message}")
            .ToList();
        var findingList = string.Join("\n", findingLines);

        return $"""
            Task description:
            {brief}

            Reviewer findings from the previous iteration — address each one:
            {findingList}

            Revise the crew configuration and call {CrewSpecTool.ToolName} with the updated design.
            """;
    }

    private static IReadOnlyList<Finding> ExtractPreviousFindings(IRunContext ctx)
    {
        if (ctx.TryGet(GeefKeys.IterationHistory, out var history)
            && history is not null
            && history.Records.Count > 0)
        {
            return history.Records[^1].EvaluationResult.AllFindings;
        }
        return [];
    }

    private static string EscapeForJson(string raw) =>
        raw.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
