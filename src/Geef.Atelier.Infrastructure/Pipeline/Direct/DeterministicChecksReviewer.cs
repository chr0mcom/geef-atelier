using System.Globalization;
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
/// <see cref="FindingSeverity.Info"/>; without failed checks the decision is <see cref="ReviewDecision.Approved"/>. In a run that
/// changes an earlier version (<see cref="AtelierContextKeys.RevisionHint"/>) the length the first request asked for is only a note
/// (<see cref="Evaluate"/>).
/// </summary>
internal sealed class DeterministicChecksReviewer : IReviewer
{
    public const string ReviewerName = "deterministic-checks";
    public const string FindingCategory = "deterministic-check";

    /// <summary>
    /// Message of a length the text misses in a run that changes an earlier version: <c>{0}</c> is the count, <c>{1}</c> the unit
    /// (words or characters), <c>{2}</c> the passage of the request the length was read from.
    /// </summary>
    public const string RevisionLengthNoteFormat =
        "The text has {0} {1}; your first request asked for \"{2}\". Because you asked for a change, the length was not corrected.";

    /// <summary>
    /// The checks of <paramref name="draft"/> (<see cref="DraftCheckEvaluator.Evaluate"/>). With <paramref name="revision"/> a length
    /// the text misses never blocks and carries <see cref="RevisionLengthNoteFormat"/>, because the change the person asked for may
    /// change the length; every other check stays as it is.
    /// </summary>
    internal static IReadOnlyList<DraftCheckResult> Evaluate(string draft, OrderConstraints constraints, bool revision)
    {
        var results = DraftCheckEvaluator.Evaluate(draft, constraints);
        if (!revision || constraints.Length is not { } length)
            return results;
        return results
            .Select(r => r is { Code: DraftCheckCodes.Length, Passed: false } ? r with { Blocking = false, Message = RevisionLengthNote(draft, length) } : r)
            .ToList();
    }

    private static string RevisionLengthNote(string draft, LengthConstraint length)
    {
        var words = length.Unit == LengthUnit.Words;
        var count = words ? DraftCheckEvaluator.CountWords(draft) : DraftCheckEvaluator.CountCharacters(draft);
        return string.Format(CultureInfo.InvariantCulture, RevisionLengthNoteFormat,
            count.ToString("N0", CultureInfo.InvariantCulture), words ? "words" : "characters", length.Quote);
    }

    /// <inheritdoc/>
    public string Name => ReviewerName;

    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var draft = context.TryGet(AtelierContextKeys.CurrentDraft, out var current) ? current ?? string.Empty : string.Empty;
        var constraints = context.TryGet(AtelierContextKeys.OrderConstraints, out var c) && c is not null ? c : OrderConstraints.None;
        var revision = context.TryGet(AtelierContextKeys.RevisionHint, out var hint) && !string.IsNullOrWhiteSpace(hint);

        var results = Evaluate(draft, constraints, revision);
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
