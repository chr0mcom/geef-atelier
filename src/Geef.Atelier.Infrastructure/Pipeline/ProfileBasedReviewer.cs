using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Profiles;
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
    ICostAccumulator? costAccumulator = null) : IReviewer
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
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider);
            costAccumulator.RecordActorCost(
                iter, ActorType.Reviewer, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider);
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
                    model, retryResponse.TokenUsage.InputTokens, retryResponse.TokenUsage.OutputTokens, profile.Provider);
                costAccumulator.RecordActorCost(
                    iter, ActorType.Reviewer, profile.Name, model,
                    retryResponse.TokenUsage.InputTokens, retryResponse.TokenUsage.OutputTokens, retryCostEur,
                    providerName: profile.Provider);
            }

            if (retryResponse.FinishReason == "tool_calls" && retryResponse.ToolArgumentsJson is not null)
                result = ParseToolInput(retryResponse.ToolArgumentsJson);
        }

        return result;
    }

    private ReviewResult ParseToolInput(string toolArgumentsJson)
    {
        using var doc = JsonDocument.Parse(toolArgumentsJson);
        var root      = doc.RootElement;

        if (!root.TryGetProperty("approved", out var approvedEl) ||
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

        var approved = approvedEl.GetBoolean();
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

        return new ReviewResult
        {
            ReviewerName = profile.Name,
            Decision     = approved ? ReviewDecision.Approved : ReviewDecision.Rejected,
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
