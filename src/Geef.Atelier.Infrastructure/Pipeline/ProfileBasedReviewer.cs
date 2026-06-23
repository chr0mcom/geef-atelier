using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class ProfileBasedReviewer(
    ReviewerProfile profile,
    ILlmClientResolver resolver,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null,
    IToolUseRunner? toolUseRunner = null,
    IToolDefinitionRepository? toolDefinitionRepository = null) : IReviewer
{
    public string Name => profile.Name;
    public int Priority => 0;

    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var draft = context.GetRequired(AtelierContextKeys.CurrentDraft);
        var iter  = context.GetRequired(GeefKeys.CurrentIteration);

        var userPrompt = $"""
            Briefing:
            {brief}

            Draft text to review:
            {draft}

            Use the submit_review tool to submit your evaluation.
            """;

        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        // Agentic tool-use loop path: when the profile has bound tools and the provider supports it.
        if (profile.ToolNames is { Count: > 0 } toolNames
            && toolUseRunner is not null
            && toolDefinitionRepository is not null
            && resolver.SupportsAgenticTools(profile.Provider))
        {
            var boundTools = await ResolveToolsAsync(toolNames, toolDefinitionRepository, cancellationToken);

            Guid.TryParse(
                context.TryGet(GeefKeys.RunId, out var ridStr) ? ridStr : null,
                out var runGuid);

            var loopCtx = new ToolInvocationContext(
                RunId: runGuid,
                IterationNumber: iter,
                ActorType: "reviewer",
                ActorName: profile.Name,
                Sequence: 0);

            var loopResult = await toolUseRunner.RunAsync(
                client, model,
                profile.SystemPrompt,
                userPrompt,
                boundTools,
                requiredFinalTool: "submit_review",
                new ToolLoopOptions { MaxToolCalls = toolNames.Count * 3 },
                loopCtx,
                cancellationToken);

            // When the required tool was called, FinalText contains the ArgumentsJson.
            var argsJson = loopResult.EndReason == ToolLoopEndReason.RequiredToolCalled
                ? loopResult.FinalText
                : null;

            return ParseToolInput(argsJson ?? "{}");
        }

        // Standard single-shot path.
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = profile.SystemPrompt,
            UserPrompt   = userPrompt,
            MaxTokens    = maxTokens,
            Tools        = [ReviewerToolDefinition.SubmitReview],
            ToolChoice   = "function:submit_review"
        }, cancellationToken);

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);
            costAccumulator.RecordActorCost(
                iter, ActorType.Reviewer, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0,
                reasoningTokens: response.TokenUsage.ReasoningTokens ?? 0);
        }

        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
        {
            return new ReviewResult
            {
                ReviewerName       = profile.Name,
                Decision           = ReviewDecision.Failed,
                Findings           = [],
                Duration           = TimeSpan.Zero,
                SuggestedRetryHint = $"Reviewer did not call submit_review (finish_reason='{response.FinishReason}')."
            };
        }

        var result = ParseToolInput(response.ToolArgumentsJson);

        // Enforce mandatory findings: approved with zero findings is a shallow review.
        // Retry once with an explicit reminder before accepting the empty-findings result.
        if (result.Decision == ReviewDecision.Approved && result.Findings.Count == 0)
        {
            const string mandatoryFindingsReminder =
                "\n\nIMPORTANT: You submitted approved=true with zero findings. Every review MUST include " +
                "at least one finding. Use 'info' severity for minor observations or suggestions. " +
                "Re-submit using the submit_review tool now.";

            var retryResponse = await client.CompleteAsync(new LlmRequest
            {
                Model        = model,
                SystemPrompt = profile.SystemPrompt,
                UserPrompt   = userPrompt + mandatoryFindingsReminder,
                MaxTokens    = maxTokens,
                Tools        = [ReviewerToolDefinition.SubmitReview],
                ToolChoice   = "function:submit_review"
            }, cancellationToken);

            if (costAccumulator is not null)
            {
                var retryCostEur = pricingCatalog?.CalculateCostEur(
                    model, retryResponse.TokenUsage.InputTokens, retryResponse.TokenUsage.OutputTokens, profile.Provider,
                    cachedInputTokens: retryResponse.TokenUsage.CachedInputTokens ?? 0);
                costAccumulator.RecordActorCost(
                    iter, ActorType.Reviewer, profile.Name, model,
                    retryResponse.TokenUsage.InputTokens, retryResponse.TokenUsage.OutputTokens, retryCostEur,
                    providerName: profile.Provider,
                    cachedInputTokens: retryResponse.TokenUsage.CachedInputTokens ?? 0,
                    reasoningTokens: retryResponse.TokenUsage.ReasoningTokens ?? 0);
            }

            if (retryResponse.FinishReason == "tool_calls" && retryResponse.ToolArgumentsJson is not null)
                result = ParseToolInput(retryResponse.ToolArgumentsJson);
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<IReadOnlyList<ToolDefinition>> ResolveToolsAsync(
        IReadOnlyList<string> toolNames,
        IToolDefinitionRepository repository,
        CancellationToken ct)
    {
        var tools = new List<ToolDefinition>(toolNames.Count);
        foreach (var name in toolNames)
        {
            var tool = await repository.GetByNameAsync(name, ct);
            if (tool is not null)
                tools.Add(tool);
        }
        return tools;
    }

    private ReviewResult ParseToolInput(string toolArgumentsJson)
    {
        using var doc = JsonDocument.Parse(toolArgumentsJson);
        var root      = doc.RootElement;

        if (!root.TryGetProperty("approved", out _) ||
            !root.TryGetProperty("findings", out var findingsEl))
        {
            return new ReviewResult
            {
                ReviewerName       = profile.Name,
                Decision           = ReviewDecision.Failed,
                Findings           = [],
                Duration           = TimeSpan.Zero,
                SuggestedRetryHint = "submit_review tool input missing 'approved' or 'findings' field."
            };
        }

        var findings = new List<Finding>();

        foreach (var f in findingsEl.EnumerateArray())
        {
            var severityStr = f.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() ?? "minor" : "minor";
            var message     = f.TryGetProperty("message",  out var msgEl) ? msgEl.GetString() ?? ""       : "";
            if (string.IsNullOrEmpty(message)) continue;

            findings.Add(new Finding
            {
                ReviewerName      = profile.Name,
                Fingerprint       = ComputeFingerprint(message),
                Message           = message,
                Severity          = MapSeverity(severityStr),
                Category          = "review",
                ArtifactReference = "draft",
                Metadata          = new Dictionary<string, object>()
            });
        }

        // The model's raw `approved` flag is intentionally ignored — blocking is determined by
        // DefaultConvergencePolicy.BlockingSeverity (default Error), not by the reviewer's own decision.
        var decision = findings.Count == 0
            ? ReviewDecision.Approved
            : ReviewDecision.ApprovedWithWarnings;

        return new ReviewResult
        {
            ReviewerName = profile.Name,
            Decision     = decision,
            Findings     = findings,
            Duration     = TimeSpan.Zero
        };
    }

    private string ComputeFingerprint(string message)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return $"{profile.Name}:{Convert.ToBase64String(hash)[..12]}";
    }

    private static SdkSeverity MapSeverity(string s) => s.ToLowerInvariant() switch
    {
        "critical" => SdkSeverity.Critical,
        "major"    => SdkSeverity.Error,
        "minor"    => SdkSeverity.Warning,
        "info"     => SdkSeverity.Info,
        "error"    => SdkSeverity.Error,    // backwards-compat
        "warning"  => SdkSeverity.Warning,  // backwards-compat
        _          => SdkSeverity.Warning
    };
}
