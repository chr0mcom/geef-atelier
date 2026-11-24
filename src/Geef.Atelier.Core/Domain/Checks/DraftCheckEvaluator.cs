using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>
/// Checks a draft against <see cref="OrderConstraints"/> without an AI model. Messages are plain English: the first
/// sentence states what was found, the last one tells the writer what to change.
/// </summary>
public static partial class DraftCheckEvaluator
{
    /// <summary>Fewest words a draft needs to count as a text.</summary>
    public const int MinimumWords = 5;

    [GeneratedRegex(@"\[[^\[\]\n]{2,80}\]", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^\s*([-*•]|\d+[.)])\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ListLinePattern();

    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingLinePattern();

    [GeneratedRegex(@"^(Here is|Here's|Hier ist|Hier sind|Gerne|Sure|Certainly|Natürlich)\b.*:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreamblePattern();

    [GeneratedRegex(@"^[\d\s,;–-]+$|^\^", RegexOptions.CultureInvariant)]
    private static partial Regex FootnotePattern();

    /// <summary>Runs every check that applies (empty text and preamble always) and returns one result per check.</summary>
    public static IReadOnlyList<DraftCheckResult> Evaluate(string draft, OrderConstraints constraints)
    {
        var text = draft ?? string.Empty;
        var results = new List<DraftCheckResult>();

        var words = CountWords(text);
        results.Add(words < MinimumWords
            ? new DraftCheckResult(DraftCheckCodes.Empty, false, true,
                $"The text is empty or almost empty ({words} words). Write the complete text the request asks for.", null)
            : new DraftCheckResult(DraftCheckCodes.Empty, true, true, $"The text has {words} words.", null));

        var firstLine = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        results.Add(PreamblePattern().IsMatch(firstLine)
            ? new DraftCheckResult(DraftCheckCodes.Preamble, false, true,
                $"The text starts with an introduction to the reader (\"{Shorten(firstLine)}\"). Remove it and start directly with the text itself.", null)
            : new DraftCheckResult(DraftCheckCodes.Preamble, true, true, "The text starts directly.", null));

        if (constraints.Length is { } length)
            results.Add(CheckLength(text, length));

        if (constraints.Language is { } language)
        {
            var guessed = TextLanguageGuess.Guess(text);
            if (guessed is not null)
            {
                results.Add(guessed == language
                    ? new DraftCheckResult(DraftCheckCodes.Language, true, true, $"The text is in {LanguageName(language)}.", null)
                    : new DraftCheckResult(DraftCheckCodes.Language, false, true,
                        $"The text is in {LanguageName(guessed)}, but it should be in {LanguageName(language)}. Write the whole text in {LanguageName(language)}.", null));
            }
        }

        var placeholders = FindPlaceholders(text);
        if (constraints.ForbidPlaceholders)
        {
            results.Add(placeholders.Count == 0
                ? new DraftCheckResult(DraftCheckCodes.Placeholders, true, true, "The text has no placeholders.", constraints.ForbidPlaceholdersQuote)
                : new DraftCheckResult(DraftCheckCodes.Placeholders, false, true,
                    $"The text has placeholders ({string.Join(", ", placeholders.Take(5))}), but the request does not allow them (\"{constraints.ForbidPlaceholdersQuote}\"). Phrase these passages so that they do not need the missing facts.",
                    constraints.ForbidPlaceholdersQuote));
        }
        else if (placeholders.Count > 0)
        {
            results.Add(new DraftCheckResult(DraftCheckCodes.Placeholders, true, false,
                $"Fill in before sending: {string.Join(", ", placeholders)}.", null));
        }

        foreach (var rule in constraints.ForbiddenPatterns)
            results.Add(CheckPattern(text, rule));

        foreach (var rule in constraints.ForbiddenTerms)
        {
            var found = ContainsTerm(text, rule.Term, wholeWord: true);
            results.Add(found
                ? new DraftCheckResult(DraftCheckCodes.ForbiddenTerm, false, true,
                    $"The text uses \"{rule.Term}\", which the request rules out (\"{rule.Quote}\"). Rephrase every passage with \"{rule.Term}\" without it.", rule.Quote)
                : new DraftCheckResult(DraftCheckCodes.ForbiddenTerm, true, true, $"The text does not use \"{rule.Term}\".", rule.Quote));
        }

        foreach (var rule in constraints.RequiredTerms)
        {
            var found = ContainsTerm(text, rule.Term, wholeWord: false);
            results.Add(found
                ? new DraftCheckResult(DraftCheckCodes.RequiredTerm, true, true, $"The text contains \"{rule.Term}\".", rule.Quote)
                : new DraftCheckResult(DraftCheckCodes.RequiredTerm, false, true,
                    $"The text does not contain \"{rule.Term}\", which the request asks for (\"{rule.Quote}\"). Add it where it fits naturally.", rule.Quote));
        }

        return results;
    }

    /// <summary>Finds placeholders in square brackets, ignoring Markdown links, footnotes and check boxes.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string draft)
    {
        if (string.IsNullOrEmpty(draft))
            return [];

        var found = new List<string>();
        foreach (Match match in PlaceholderPattern().Matches(draft))
        {
            var end = match.Index + match.Length;
            if (end < draft.Length && draft[end] is '(' or ':' or '[')
                continue;
            if (match.Index > 0 && draft[match.Index - 1] is '[' or '!')
                continue;
            if (end < draft.Length && draft[end] == ']')
                continue;
            var inner = match.Value[1..^1].Trim();
            if (inner.Length == 0 || inner is "x" or "X" || FootnotePattern().IsMatch(inner))
                continue;
            if (!found.Contains(match.Value, StringComparer.Ordinal))
                found.Add(match.Value);
        }

        return found;
    }

    /// <summary>Counts words as runs of characters between white space that contain at least one letter or digit.</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(t => t.Any(char.IsLetterOrDigit));
    }

    /// <summary>Counts characters including spaces, without line breaks.</summary>
    public static int CountCharacters(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Trim().Count(c => c is not '\n' and not '\r');

    private static DraftCheckResult CheckLength(string text, LengthConstraint length)
    {
        var unitName = length.Unit == LengthUnit.Words ? "words" : "characters";
        var actual = length.Unit == LengthUnit.Words ? CountWords(text) : CountCharacters(text);
        var (low, high) = AcceptedBounds(length);

        var passed = (low is null || actual >= low) && (high is null || actual <= high);
        var asked = DescribeRequest(length, unitName);
        if (passed)
            return new DraftCheckResult(DraftCheckCodes.Length, true, true,
                $"The text has {Format(actual)} {unitName}; the request asks for {asked}.", length.Quote);

        var fix = high is not null && actual > high
            ? $"Shorten it to {asked} without dropping anything the request requires."
            : $"Extend it to {asked} without adding facts that are not in the request or the files.";
        return new DraftCheckResult(DraftCheckCodes.Length, false, true,
            $"The text has {Format(actual)} {unitName}. The request asks for {asked} (\"{length.Quote}\"). {fix}", length.Quote);
    }

    /// <summary>The accepted range of a length constraint after its tolerance; <c>null</c> means open.</summary>
    public static (int? Low, int? High) AcceptedBounds(LengthConstraint length) => length.Kind switch
    {
        LengthKind.AtMost => (null, Ceil(length.Max, 1.02)),
        LengthKind.AtLeast => (Floor(length.Min, 0.98), null),
        LengthKind.About => (Floor(length.Min, 0.80), Ceil(length.Max, 1.20)),
        LengthKind.Stated => (Floor(length.Min, 0.85), Ceil(length.Max, 1.15)),
        _ => (Floor(length.Min, 0.95), Ceil(length.Max, 1.05)),
    };

    private static string DescribeRequest(LengthConstraint length, string unitName) => length.Kind switch
    {
        LengthKind.AtMost => $"at most {Format(length.Max)} {unitName}",
        LengthKind.AtLeast => $"at least {Format(length.Min)} {unitName}",
        LengthKind.About when length.Min != length.Max => $"about {Format(length.Min)}–{Format(length.Max)} {unitName}",
        LengthKind.About => $"about {Format(length.Min)} {unitName}",
        LengthKind.Stated => $"{Format(length.Min)} {unitName}",
        _ => $"between {Format(length.Min)} and {Format(length.Max)} {unitName}",
    };

    private static DraftCheckResult CheckPattern(string text, PatternRule rule)
    {
        var (code, found, what) = rule.Pattern switch
        {
            ForbiddenPattern.Lists => (DraftCheckCodes.Lists, ListLinePattern().IsMatch(text), "lists or bullet points"),
            ForbiddenPattern.Headings => (DraftCheckCodes.Headings, HeadingLinePattern().IsMatch(text), "headings"),
            _ => (DraftCheckCodes.Emojis, ContainsEmoji(text), "emojis"),
        };
        var fix = rule.Pattern switch
        {
            ForbiddenPattern.Lists => "Turn every list into continuous sentences.",
            ForbiddenPattern.Headings => "Remove the headings and keep the text as continuous prose.",
            _ => "Remove every emoji.",
        };
        return found
            ? new DraftCheckResult(code, false, true, $"The text has {what}, but the request rules them out (\"{rule.Quote}\"). {fix}", rule.Quote)
            : new DraftCheckResult(code, true, true, $"The text has no {what}.", rule.Quote);
    }

    private static bool ContainsTerm(string text, string term, bool wholeWord)
    {
        var escaped = Regex.Escape(term);
        var pattern = wholeWord
            ? $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])"
            : $@"(?<![\p{{L}}\p{{N}}]){escaped}";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsEmoji(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x1F000 and <= 0x1FAFF
                or >= 0x2600 and <= 0x27BF
                or 0x2B50 or 0x2B55 or 0x2B06 or 0x2B07 or 0x2B05 or 0x2B1B or 0x2B1C
                or 0x231A or 0x231B or 0x23F0 or 0x23F3)
                return true;
        }
        return false;
    }

    private static int? Floor(int? value, double factor) =>
        value is null ? null : (int)Math.Floor(value.Value * factor);

    private static int? Ceil(int? value, double factor) =>
        value is null ? null : (int)Math.Ceiling(value.Value * factor);

    private static string Format(int? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "?";

    private static string LanguageName(string code) => code switch
    {
        "de" => "German",
        "en" => "English",
        _ => code,
    };

    private static string Shorten(string value) =>
        value.Length <= 80 ? value : value[..80] + "…";
}
