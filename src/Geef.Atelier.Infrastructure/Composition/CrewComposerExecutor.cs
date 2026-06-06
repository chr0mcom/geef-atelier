using System.Text;
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
/// The live model catalog is injected via <see cref="IModelCatalog"/> (24h-cached, static fallback)
/// so the LLM only ever sees currently valid provider/model combinations.
/// </remarks>
internal sealed class CrewComposerExecutor(
    ExecutorProfile profile,
    ILlmClientResolver llmClientResolver,
    IModelCatalog modelCatalog,
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

        // Inject live model catalog so the LLM never hallucinates provider/model names.
        var modelCatalogBlock = await BuildModelCatalogBlockAsync(cancellationToken);

        // The system prompt comes entirely from the profile (SystemPrompts.CrewComposerExecutor).
        // We append the live model catalog block and the crew catalog grounding.
        var systemPrompt = $"{profile.SystemPrompt}\n\n{modelCatalogBlock}\n\n{groundedContext}".TrimEnd();

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
    // Model catalog block
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Fetches the live provider and model lists and builds two Markdown sections:
    /// (1) recommended models (curated shortlist filtered against live catalog),
    /// (2) all valid (provider, model) pairs.
    /// Falls back to <see cref="StaticModelFallback"/> when a provider is unreachable
    /// (IModelCatalog handles this internally via its 24-hour cache).
    /// </summary>
    private async Task<string> BuildModelCatalogBlockAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Valid Provider/Model Pairs (ONLY use these — never invent names)");
        sb.AppendLine();
        sb.AppendLine("Provider names and model IDs are exact strings — any deviation fails validation.");
        sb.AppendLine();

        // Providers relevant for LLM roles (grounding/deterministic finalizers need no model).
        string[] llmProviders = ["claude-cli", "codex-cli", "openrouter", "openai-direct", "google-ai-studio", "deepseek", "xai"];

        // Collect live models per provider.
        var liveModels = new Dictionary<string, IReadOnlyList<ModelInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerName in llmProviders)
        {
            try
            {
                var models = await modelCatalog.ListModelsAsync(providerName, ct);
                if (models.Count > 0)
                    liveModels[providerName] = models;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CrewComposerExecutor: could not fetch models for provider '{Provider}'", providerName);
            }
        }

        // Section 1: Recommended shortlist (curated × live).
        sb.AppendLine("### Recommended models (prefer these — newest top-tier, subscription where available)");
        sb.AppendLine();
        sb.AppendLine("| Provider | Model | Role hint |");
        sb.AppendLine("|---|---|---|");

        bool executorListed = false;
        if (liveModels.TryGetValue(PreferredComposerModels.Executor.Provider, out var execModels)
            && execModels.Any(m => string.Equals(m.Id, PreferredComposerModels.Executor.Model, StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine($"| `{PreferredComposerModels.Executor.Provider}` | `{PreferredComposerModels.Executor.Model}` | **Executor** (subscription, zero token cost) |");
            executorListed = true;
        }

        int reviewerIdx = 0;
        foreach (var (prov, mod) in PreferredComposerModels.Reviewers)
        {
            if (liveModels.TryGetValue(prov, out var provModels)
                && provModels.Any(m => string.Equals(m.Id, mod, StringComparison.OrdinalIgnoreCase)))
            {
                var hint = reviewerIdx == 0 ? "**Reviewer** (subscription, zero token cost)"
                         : "**Reviewer** (OpenRouter, pay-per-token)";
                sb.AppendLine($"| `{prov}` | `{mod}` | {hint} |");
                reviewerIdx++;
            }
        }

        if (!executorListed)
        {
            // Static fallback if preferred executor not in live catalog.
            sb.AppendLine($"| `claude-cli` | `claude-opus-4-8` | **Executor** (subscription) |");
        }

        sb.AppendLine();
        sb.AppendLine("**Model plurality rule:** reviewer `model` values MUST differ from the executor `model`.");
        sb.AppendLine("**Reuse rule:** prefer `reuse: \"default-executor\"` for executor and `reuse: \"learning-extractor\"` for the output finalizer — these are always valid without a provider/model.");
        sb.AppendLine();

        // Section 2: Full valid catalog per provider (limited to avoid prompt bloat).
        sb.AppendLine("### Full valid catalog (only use exact IDs listed here)");
        sb.AppendLine();

        // CLI providers: always show in full (small lists).
        foreach (var prov in new[] { "claude-cli", "codex-cli" })
        {
            if (!liveModels.TryGetValue(prov, out var models)) continue;
            sb.AppendLine($"**{prov}:** {string.Join(", ", models.Select(m => $"`{m.Id}`"))}");
        }

        // OpenRouter: too many models — show curated subset (preferred + top known ones).
        if (liveModels.TryGetValue("openrouter", out var orModels))
        {
            // Known-good top models ordered newest first; supplement with any preferred that made it through.
            var topOpenRouter = new[]
            {
                "x-ai/grok-4.3", "x-ai/grok-4.20",
                "google/gemini-3.1-pro-preview", "google/gemini-3.5-flash", "google/gemini-3.1-flash-lite",
                "openai/gpt-5.5", "openai/gpt-5.5-pro", "openai/gpt-5.4", "openai/gpt-5.4-pro",
                "anthropic/claude-opus-4.8", "anthropic/claude-sonnet-4.5",
                "deepseek/deepseek-v4-pro", "deepseek/deepseek-v3.2", "deepseek/deepseek-r1-0528",
                "mistralai/mistral-large-2512", "meta-llama/llama-4-maverick",
            };
            var available = topOpenRouter
                .Where(id => orModels.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Also include any preferred reviewer models not already in topOpenRouter.
            foreach (var (_, mod) in PreferredComposerModels.Reviewers.Where(r => r.Provider == "openrouter"))
            {
                if (!available.Contains(mod, StringComparer.OrdinalIgnoreCase)
                    && orModels.Any(m => string.Equals(m.Id, mod, StringComparison.OrdinalIgnoreCase)))
                    available.Add(mod);
            }

            if (available.Count > 0)
                sb.AppendLine($"**openrouter (selection):** {string.Join(", ", available.Select(id => $"`{id}`"))}");
        }

        // Other direct providers: show in full (usually small lists).
        foreach (var prov in new[] { "openai-direct", "google-ai-studio", "deepseek", "xai" })
        {
            if (!liveModels.TryGetValue(prov, out var models) || models.Count == 0) continue;
            var ids = models.Take(8).Select(m => $"`{m.Id}`");
            sb.AppendLine($"**{prov}:** {string.Join(", ", ids)}");
        }

        sb.AppendLine();
        sb.AppendLine("**IMPORTANT:** Never use provider names like `openai`, `google`, `anthropic`, `x-ai` — these are not valid Geef providers. Use only the exact names above.");

        // Section 3: Valid grounding provider_type discriminators (config-driven, not LLM models).
        var groundingTypes = groundingProviderFactory.RegisteredTypes.OrderBy(t => t).ToList();
        if (groundingTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Valid grounding provider_type values (ONLY use these for grounding_providers)");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", groundingTypes.Select(t => $"`{t}`")));
            sb.AppendLine();
            sb.AppendLine("For web/literature search use `tavily` (general web), `academic-search` (papers/arXiv/Semantic Scholar) or `news-search` — there is NO `web` type. " +
                          "`provider_type` is REQUIRED on every inline grounding provider and must be one of the exact values above.");
        }

        return sb.ToString();
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
            Remember: use only provider/model pairs from the catalog in your system prompt.

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
