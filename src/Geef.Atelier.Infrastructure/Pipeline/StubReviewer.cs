using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class StubReviewer : IReviewer
{
    private readonly string _name;
    private readonly FindingSeverity _severity;
    private readonly string _message;

    public StubReviewer(string name, FindingSeverity severity, string message)
    {
        _name = name;
        _severity = severity;
        _message = message;
    }

    public string Name => _name;
    public int Priority => 0;

    public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var iter = context.GetRequired(GeefKeys.CurrentIteration);

        if (iter == 1)
        {
            var finding = new Finding
            {
                ReviewerName = _name,
                Fingerprint   = $"{_name}:stub:iter1",
                Message       = _message,
                Severity      = _severity,
                Category      = "stub",
                ArtifactReference = "draft",
                Metadata      = new Dictionary<string, object>()
            };

            return Task.FromResult(new ReviewResult
            {
                ReviewerName = _name,
                Decision     = ReviewDecision.Rejected,
                Findings     = [finding],
                Duration     = TimeSpan.Zero
            });
        }

        return Task.FromResult(new ReviewResult
        {
            ReviewerName = _name,
            Decision     = ReviewDecision.Approved,
            Findings     = [],
            Duration     = TimeSpan.Zero
        });
    }
}
