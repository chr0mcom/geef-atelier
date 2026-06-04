using System.Text;
using System.Security.Cryptography;
using Geef.Atelier.Application.Composition;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Deterministic, non-LLM <see cref="IReviewer"/> that validates the Crew-Spec JSON artifact
/// produced during the Auto-Crew composition loop.
/// <para>
/// On each iteration the reviewer parses the current draft from the run context and runs
/// <see cref="ICrewSpecValidator"/> against it.  Any structural issue becomes a
/// <see cref="Finding"/>; critical issues use <see cref="FindingSeverity.Critical"/>,
/// non-critical issues use <see cref="FindingSeverity.Error"/>.
/// When the spec is valid, or when no draft exists yet, the reviewer returns
/// <see cref="ReviewDecision.Approved"/>.
/// </para>
/// </summary>
internal sealed class CrewSpecValidatorReviewer(ICrewSpecValidator validator) : IReviewer
{
    /// <inheritdoc/>
    public string Name => "crew-spec-validator";

    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public async Task<ReviewResult> ReviewAsync(
        IRunContext context,
        CancellationToken cancellationToken)
    {
        // Try to obtain the current artifact text from the run context.
        // ProfileBasedReviewer uses AtelierContextKeys.CurrentDraft for the same purpose.
        if (!context.TryGet(AtelierContextKeys.CurrentDraft, out var artifactText)
            || string.IsNullOrWhiteSpace(artifactText))
        {
            // Nothing to validate yet (first iteration before any execution).
            return Approved([]);
        }

        var issues = await validator.ValidateAsync(artifactText, cancellationToken);

        if (issues.Count == 0)
            return Approved([]);

        var findings = issues
            .Select(issue => new Finding
            {
                ReviewerName      = Name,
                Fingerprint       = ComputeFingerprint(issue.Field, issue.Message),
                Message           = $"[{issue.Field}] {issue.Message}",
                Severity          = issue.IsCritical ? FindingSeverity.Critical : FindingSeverity.Error,
                Category          = "crew-spec-validation",
                ArtifactReference = "draft",
                Metadata          = new Dictionary<string, object>
                {
                    ["field"]       = issue.Field,
                    ["is_critical"] = issue.IsCritical,
                },
            })
            .ToList();

        return new ReviewResult
        {
            ReviewerName = Name,
            Decision     = ReviewDecision.Rejected,
            Findings     = findings,
            Duration     = TimeSpan.Zero,
        };
    }

    // -------------------------------------------------------------------------

    private ReviewResult Approved(IReadOnlyList<Finding> findings) =>
        new()
        {
            ReviewerName = Name,
            Decision     = ReviewDecision.Approved,
            Findings     = findings,
            Duration     = TimeSpan.Zero,
        };

    /// <summary>
    /// Produces a stable fingerprint from the field path and message so that the same issue
    /// is not counted twice across iterations.
    /// </summary>
    private string ComputeFingerprint(string field, string message)
    {
        var raw  = $"{Name}:{field}:{message}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"{Name}:{Convert.ToBase64String(hash)[..12]}";
    }
}
