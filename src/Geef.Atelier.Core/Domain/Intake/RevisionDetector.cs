using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>A file of the order that could be the text to revise; <see cref="FileNumber"/> is its number in the clarifier prompt.</summary>
public sealed record RevisionCandidate(int FileNumber, Guid SourceId, string FileName);

/// <summary>Recognises a request to revise an attached text. Conservative: without a revision verb no file becomes the template.</summary>
public static class RevisionDetector
{
    /// <summary>Verb stems, matched without regard to case; "…" separates the parts of a split verb.</summary>
    public static IReadOnlyList<string> Verbs { get; } =
    [
        "überarbeit", "verbesser", "kürz", "korrigier", "redigier", "glätte", "schärf", "umschreib", "formulier … um",
        "lektorier", "prüf … und verbesser",
        "revise", "improve", "shorten", "rewrite", "edit", "proofread", "polish", "tighten", "rephrase",
    ];

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string SplitMarker = "…";
    private const int MaxSplitGap = 80;
    private const int MaxParticleLength = 4;

    private static readonly HashSet<string> EnglishVerbs = new(StringComparer.Ordinal)
    {
        "revise", "improve", "shorten", "rewrite", "edit", "proofread", "polish", "tighten", "rephrase",
    };

    private static readonly IReadOnlyDictionary<string, string> GermanExclusions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["kürz"] = "(?!lich)",
    };

    private static readonly IReadOnlyList<Regex> Patterns = Verbs.SelectMany(Build).ToList();

    /// <summary>True when the request contains a revision verb.</summary>
    public static bool MentionsRevision(string requestText) =>
        !string.IsNullOrWhiteSpace(requestText) && Patterns.Any(p => p.IsMatch(requestText));

    /// <summary>The file to revise: requires a revision verb in the request; with one file that file, with several the model's choice when valid, otherwise none.</summary>
    public static RevisionCandidate? Choose(string requestText, IReadOnlyList<RevisionCandidate> files, int? modelChoice, bool modelSaysRevision)
    {
        if (files.Count == 0 || !MentionsRevision(requestText))
            return null;
        if (files.Count == 1)
            return files[0];
        if (!modelSaysRevision || modelChoice is not { } number)
            return null;
        var matches = files.Where(f => f.FileNumber == number).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IEnumerable<Regex> Build(string verb)
    {
        if (EnglishVerbs.Contains(verb))
        {
            var forms = verb.EndsWith('e')
                ? $"{Regex.Escape(verb[..^1])}(?:e|es|ed|ing)"
                : $"{Regex.Escape(verb)}(?:s|es|ed|ing)?";
            yield return new Regex($@"(?<![\p{{L}}]){forms}(?![\p{{L}}])", Options);
            yield break;
        }

        var parts = verb.Split(SplitMarker, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            var exclusion = GermanExclusions.TryGetValue(verb, out var e) ? e : string.Empty;
            yield return new Regex($@"(?<![\p{{L}}]){Regex.Escape(verb)}{exclusion}\p{{L}}*", Options);
            yield break;
        }

        var first = Regex.Escape(parts[0]);
        var last = Regex.Escape(parts[^1]);
        var particle = parts[^1].Length <= MaxParticleLength && !parts[^1].Contains(' ');
        var lastEnd = particle ? @"(?![\p{L}])" : @"\p{L}*";
        yield return new Regex(
            $@"(?<![\p{{L}}]){first}\p{{L}}*(?![\p{{L}}])[^.!?\n]{{1,{MaxSplitGap}}}?(?<![\p{{L}}]){last}{lastEnd}", Options);
        if (particle)
            yield return new Regex($@"(?<![\p{{L}}]){last}{first}\p{{L}}*", Options);
    }
}
