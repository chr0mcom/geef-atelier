using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
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
/// This executor is the analog of <c>ProfileBasedExecutor</c> but for the Auto-Crew composition
/// pipeline. Instead of generating free-form text, it forces a structured tool call so the output
/// can be deterministically parsed and validated by the downstream reviewer step.
/// The <see cref="ExecutorProfile"/> is supplied at construction time by the orchestrator
/// (via <c>ActivatorUtilities.CreateInstance</c>) so each run gets the correct profile.
/// The composer chooses roles (<c>model_slot</c>) instead of models; the model-assignment block comes from
/// <see cref="ComposerModelCatalog"/> and models are assigned from the model matrix (D-066).
/// </remarks>
internal sealed class CrewComposerExecutor(
    ExecutorProfile profile,
    ILlmClientResolver llmClientResolver,
    IGroundingProviderFactory groundingProviderFactory,
    ILogger<CrewComposerExecutor> logger,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null) : IExecutionStep
{
    /// <inheritdoc />
    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter  = context.GetRequired(GeefKeys.CurrentIteration);

        var groundedContext = string.Empty;
        if (context.TryGet(AtelierContextKeys.GroundingContext, out var groundingCtx) && groundingCtx is not null)
            groundedContext = groundingCtx;

        var modelCatalogBlock = ComposerModelCatalog.Build(groundingProviderFactory.RegisteredTypes);

        var systemPrompt = $"{profile.SystemPrompt}\n\n{modelCatalogBlock}\n\n{groundedContext}".TrimEnd();

        var userPrompt = BuildUserPrompt(context, brief, iter);

        var (client, model, maxTokens) = llmClientResolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        logger.LogInformation(
            "CrewComposerExecutor: calling {Provider}/{Model} iter={Iter} maxTokens={MaxTokens}",
            profile.Provider, model, iter, maxTokens);

        // Prefer server-validated response_format json_schema when the provider supports it
        // (cli-proxy / OpenRouter); otherwise fall back to the forced-tool JSON hack.
        var supportsStructured = llmClientResolver.SupportsStructuredOutputs(profile.Provider);
        var (tools, toolChoice, responseFormat) = StructuredOutput.Build(CrewSpecTool.Schema, supportsStructured);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model          = model,
            SystemPrompt   = systemPrompt,
            UserPrompt     = userPrompt,
            MaxTokens      = maxTokens,
            Tools          = tools,
            ToolChoice     = toolChoice,
            ResponseFormat = responseFormat
        }, cancellationToken);

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);
            costAccumulator.RecordActorCost(
                iter, ActorType.Executor, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0,
                reasoningTokens: response.TokenUsage.ReasoningTokens ?? 0);
        }

        var crewJson = StructuredOutput.ExtractJson(response);
        string artifact;
        if (crewJson is null)
        {
            logger.LogWarning(
                "CrewComposerExecutor: LLM produced no structured output (finish_reason='{FinishReason}'). Producing error artifact.",
                response.FinishReason);

            artifact = $"{{\"_error\": \"LLM did not produce a {CrewSpecTool.ToolName} object (finish_reason='{response.FinishReason}'). Raw response: {EscapeForJson(response.Text)}\"}}";
        }
        else
        {
            logger.LogInformation(
                "CrewComposerExecutor: structured output received, tokens_in={In} tokens_out={Out}",
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens);

            artifact = crewJson;
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
                $"structured_ok={crewJson is not null}"
            ]
        };
    }

    // ---------------------------------------------------------------------------
    // User prompt
    // ---------------------------------------------------------------------------

    private const string TaxonomyReminder = """
        REMINDER — copy these EXACT 5 lines verbatim into EVERY new (non-reused) reviewer system_prompt:
        - critical: substantial factual or logical error; the reader is actively misinformed.
        - major: important omission or clear inaccuracy that significantly reduces usefulness.
        - minor: style improvement, request for precision; substantially correct.
        - info: optional note; no action required.
        Anti-pattern: "technically correct" != critical. If correct but could be more precise -> minor at most.
        """;

    private static string BuildUserPrompt(IRunContext context, string brief, int iter)
    {
        if (iter == 1)
            return $"""
                Task description:
                {brief}

                Design the optimal crew configuration for this task and call {CrewSpecTool.ToolName}.

                {TaxonomyReminder}
                """;

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
            Remember: set model_slot on every inline LLM part — never provider/model.

            {TaxonomyReminder}
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
