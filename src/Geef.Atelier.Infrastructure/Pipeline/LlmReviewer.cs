using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.Options;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class LlmReviewer(
    string name,
    string systemPrompt,
    ILlmClient client,
    IOptions<LlmOptions> options) : IReviewer
{
    public string Name => name;
    public int Priority => 0;

    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var draft = context.GetRequired(AtelierContextKeys.CurrentDraft);

        var userPrompt = $"""
            Briefing:
            {brief}

            Draft text to review:
            {draft}

            Use the submit_review tool to submit your evaluation.
            """;

        var actorCfg  = options.Value.Actors.GetValueOrDefault(name);
        var model     = actorCfg?.Model is { Length: > 0 } m ? m : options.Value.DefaultModel;
        var maxTokens = actorCfg?.MaxTokens ?? options.Value.DefaultMaxTokens;

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = systemPrompt,
            UserPrompt   = userPrompt,
            MaxTokens    = maxTokens,
            Tools        = [ReviewerToolDefinition.SubmitReview],
            ToolChoice   = "function:submit_review"
        }, cancellationToken);

        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
        {
            return new ReviewResult
            {
                ReviewerName       = name,
                Decision           = ReviewDecision.Failed,
                Findings           = [],
                Duration           = TimeSpan.Zero,
                SuggestedRetryHint = $"Reviewer did not call submit_review (finish_reason='{response.FinishReason}')."
            };
        }

        return ParseToolInput(response.ToolArgumentsJson);
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
                ReviewerName       = name,
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
            var severityStr = f.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() ?? "warning" : "warning";
            var message     = f.TryGetProperty("message",  out var msgEl) ? msgEl.GetString() ?? ""        : "";
            if (string.IsNullOrEmpty(message)) continue;

            findings.Add(new Finding
            {
                ReviewerName      = name,
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
            ReviewerName = name,
            Decision     = approved ? ReviewDecision.Approved : ReviewDecision.Rejected,
            Findings     = findings,
            Duration     = TimeSpan.Zero
        };
    }

    private string ComputeFingerprint(string message)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return $"{name}:{Convert.ToBase64String(hash)[..12]}";
    }

    private static SdkSeverity MapSeverity(string s) => s.ToLowerInvariant() switch
    {
        "info"     => SdkSeverity.Info,
        "warning"  => SdkSeverity.Warning,
        "error"    => SdkSeverity.Error,
        "critical" => SdkSeverity.Critical,
        _          => SdkSeverity.Warning
    };
}
