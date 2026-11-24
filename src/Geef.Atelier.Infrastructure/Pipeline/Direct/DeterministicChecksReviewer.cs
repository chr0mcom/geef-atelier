using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Reviewer of the direct path that checks the draft without an AI model (<see cref="DraftCheckEvaluator"/> against
/// the <see cref="OrderConstraints"/> of the order). A failed blocking check becomes an <see cref="FindingSeverity.Error"/>
/// finding and the decision <see cref="ReviewDecision.Rejected"/>, so that after the last allowed pass the SDK delivers
/// the version with fewer rejections. Non-blocking results (placeholders to fill in) become
/// <see cref="FindingSeverity.Info"/>; without failed checks the decision is <see cref="ReviewDecision.Approved"/>.
/// </summary>
internal sealed class DeterministicChecksReviewer : IReviewer
{
    public const string ReviewerName = "deterministic-checks";
    public const string FindingCategory = "deterministic-check";

    /// <inheritdoc/>
    public string Name => ReviewerName;

    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var draft = context.TryGet(AtelierContextKeys.CurrentDraft, out var current) ? current ?? string.Empty : string.Empty;
        var constraints = context.TryGet(AtelierContextKeys.OrderConstraints, out var c) && c is not null ? c : OrderConstraints.None;

        var results = DraftCheckEvaluator.Evaluate(draft, constraints);
        var findings = new List<Finding>();
        foreach (var result in results)
        {
            if (!result.Passed && result.Blocking)
                findings.Add(ToFinding(result, FindingSeverity.Error));
            else if (!result.Blocking)
                findings.Add(ToFinding(result, FindingSeverity.Info));
        }

        var rejected = findings.Any(f => f.Severity == FindingSeverity.Error);
        return Task.FromResult(new ReviewResult
        {
            ReviewerName = Name,
            Decision = rejected ? ReviewDecision.Rejected : ReviewDecision.Approved,
            Findings = findings,
            Duration = TimeSpan.Zero,
        });
    }

    private Finding ToFinding(DraftCheckResult result, FindingSeverity severity) => new()
    {
        ReviewerName = Name,
        Fingerprint = Fingerprint(result),
        Message = result.Message,
        Severity = severity,
        Category = FindingCategory,
        ArtifactReference = "draft",
        Metadata = new Dictionary<string, object>
        {
            ["code"] = result.Code,
            ["blocking"] = result.Blocking,
        },
    };

    private string Fingerprint(DraftCheckResult result)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{result.Code}:{result.Quote}"));
        return $"{Name}:{Convert.ToBase64String(hash)[..12]}";
    }
}
