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

        try
        {
            var response = await LlmResilience.ExecuteAsync(
                ct => client.CompleteAsync(new LlmRequest
                {
                    Model        = model,
                    SystemPrompt = profile.SystemPrompt,
                    UserPrompt   = userPrompt,
                    MaxTokens    = maxTokens,
                    Tools        = [ReviewerToolDefinition.SubmitReview],
                    ToolChoice   = "function:submit_review"
                }, ct),
                cancellationToken,
                maxAttempts: LlmResilience.ReviewerMaxAttempts,
                maxDelay: LlmResilience.ReviewerMaxDelay);

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

                var retryResponse = await LlmResilience.ExecuteAsync(
                    ct => client.CompleteAsync(new LlmRequest
                    {
                        Model        = model,
                        SystemPrompt = profile.SystemPrompt,
                        UserPrompt   = userPrompt + mandatoryFindingsReminder,
                        MaxTokens    = maxTokens,
                        Tools        = [ReviewerToolDefinition.SubmitReview],
                        ToolChoice   = "function:submit_review"
                    }, ct),
                    cancellationToken,
                    maxAttempts: LlmResilience.ReviewerMaxAttempts,
                    maxDelay: LlmResilience.ReviewerMaxDelay);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine run cancellation / shutdown — must propagate, not be masked as a skip.
            throw;
        }
        catch (Exception ex)
        {
            // The reviewer's provider was unavailable even after retries (or returned a permanent
            // error such as auth / bad request). A single reviewer failure must NEVER abort a
            // multi-iteration run: skip this reviewer for this round with one non-blocking info
            // finding. It is attempted again on the next iteration and may well succeed then.
            return SkippedDueToUnavailability(LlmResilience.Describe(ex));
        }
    }

    /// <summary>
    /// Builds a non-blocking review result for a reviewer that could not be reached this round.
    /// Carries a single <see cref="SdkSeverity.Info"/> finding so the outage is recorded and visible
    /// without ever blocking convergence.
    /// </summary>
    private ReviewResult SkippedDueToUnavailability(string reason) => new()
    {
        ReviewerName = profile.Name,
        Decision     = ReviewDecision.ApprovedWithWarnings,
        Findings     =
        [
            new Finding
            {
                ReviewerName      = profile.Name,
                Fingerprint       = ComputeFingerprint($"__reviewer_skipped__:{reason}"),
                Message           = $"Reviewer '{profile.DisplayName}' was temporarily unavailable and was "
                                    + $"skipped this round ({reason}). This is non-blocking — the reviewer "
                                    + "runs again on the next iteration.",
                Severity          = SdkSeverity.Info,
                Category          = "reviewer-availability",
                ArtifactReference = "draft",
                Metadata          = new Dictionary<string, object> { ["skipped"] = true, ["reason"] = reason }
            }
        ],
        Duration     = TimeSpan.Zero
    };

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

        // Severity is the single source of truth for convergence — it matches the documented taxonomy
        // (critical/major block; minor/info do not) and avoids the self-contradictory "approve only when
        // findings is empty" trap, which combined with the mandatory-findings rule made convergence
        // impossible (every reviewer always has ≥1 finding ⇒ always Rejected ⇒ StopMaxAttemptsReached
        // at any iteration budget). The model's raw `approved` flag is intentionally ignored.
        // SDK severities: Info, Warning (=minor), Error (=major), Critical. Only Error/Critical block.
        var hasBlocking = findings.Any(f =>
            f.Severity == SdkSeverity.Critical || f.Severity == SdkSeverity.Error);

        var decision = hasBlocking
            ? ReviewDecision.Rejected
            : findings.Count == 0
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
