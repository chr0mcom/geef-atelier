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
/// changes an earlier version (<see cref="AtelierContextKeys.RevisionHint"/>) every rule read from the first request is only a note
/// (<see cref="Evaluate"/>).
/// </summary>
internal sealed class DeterministicChecksReviewer : IReviewer
{
    public const string ReviewerName = "deterministic-checks";
    public const string FindingCategory = "deterministic-check";

    /// <summary>
    /// Note on a rule of the first request the changed text does not keep: <c>{0}</c> is what the first request asked for (its words in
    /// quotes, or the language), <c>{1}</c> what the new version does instead.
    /// </summary>
    public const string FirstRequestNoteFormat =
        "Your first request asked for {0}; the new version does not keep to it: {1}. Please check whether your change meant this.";

    /// <summary>
    /// The checks of <paramref name="draft"/> (<see cref="DraftCheckEvaluator.Evaluate"/>). With <paramref name="revision"/> every rule
    /// read from the first request (<see cref="DraftCheckResultExtensions.IsFirstRequestRule"/>: length, language, no placeholders, forbidden forms, forbidden and
    /// required words) never blocks, because the change the person asked for may replace it; one the text misses carries
    /// <see cref="FirstRequestNoteFormat"/>. The checks that always apply (not empty, no remark before the text, the placeholders to fill
    /// in, the calculations) stay as they are.
    /// </summary>
    internal static IReadOnlyList<DraftCheckResult> Evaluate(string draft, OrderConstraints constraints, bool revision)
    {
        var results = DraftCheckEvaluator.Evaluate(draft, constraints);
        if (!revision)
            return results;

        var forbidden = new Queue<TermRule>(constraints.ForbiddenTerms);
        var required = new Queue<TermRule>(constraints.RequiredTerms);
        var mapped = new List<DraftCheckResult>(results.Count);
        foreach (var result in results)
        {
            var rule = result.Code switch
            {
                DraftCheckCodes.ForbiddenTerm => forbidden.TryDequeue(out var f) ? f : null,
                DraftCheckCodes.RequiredTerm => required.TryDequeue(out var r) ? r : null,
                _ => null,
            };
            if (!result.IsFirstRequestRule() || result.Passed)
                mapped.Add(result);
            else
                mapped.Add(result with { Blocking = false, Message = FirstRequestNote(draft, constraints, result, rule) });
        }
        return mapped;
    }

    private static string FirstRequestNote(string draft, OrderConstraints constraints, DraftCheckResult result, TermRule? rule)
    {
        var asked = result.Code == DraftCheckCodes.Language && constraints.Language is { } language
            ? "a text in " + DraftCheckEvaluator.LanguageName(language)
            : $"\"{result.Quote ?? constraints.Length?.Quote ?? string.Empty}\"";
        var instead = result.Code switch
        {
            DraftCheckCodes.Length when constraints.Length is { } length => length.Unit == LengthUnit.Words
                ? $"it has {DraftCheckEvaluator.CountWords(draft).ToString("N0", CultureInfo.InvariantCulture)} words"
                : $"it has {DraftCheckEvaluator.CountCharacters(draft).ToString("N0", CultureInfo.InvariantCulture)} characters",
            DraftCheckCodes.Language => TextLanguageGuess.Guess(draft) is { } guessed ? "it is in " + DraftCheckEvaluator.LanguageName(guessed) : "it is in another language",
            DraftCheckCodes.Placeholders => "it has placeholders",
            DraftCheckCodes.Lists => "it uses lists",
            DraftCheckCodes.Headings => "it uses headings",
            DraftCheckCodes.Emojis => "it uses emojis",
            DraftCheckCodes.ForbiddenTerm => rule is null ? "it uses words that were ruled out" : $"it uses \"{rule.Term}\"",
            DraftCheckCodes.RequiredTerm => rule is null ? "it lacks words that were asked for" : $"it does not contain \"{rule.Term}\"",
            _ => "it does not pass this check",
        };
        return string.Format(CultureInfo.InvariantCulture, FirstRequestNoteFormat, asked, instead);
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
