using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Fallback reviewer used when a crew template has no reviewers configured.
/// Immediately approves every iteration so the pipeline converges in a single pass.
/// </summary>
internal sealed class AutoApproveReviewer : IReviewer
{
    public string Name => "auto-approve";
    public int Priority => 0;

    public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new ReviewResult
        {
            ReviewerName = Name,
            Decision     = ReviewDecision.Approved,
            Findings     = [],
            Duration     = TimeSpan.Zero,
        });
}
