using System.Globalization;
using System.Text;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>Topics that may go to the search, and how many were withheld or over the limit.</summary>
public sealed record QuerySelection(IReadOnlyList<string> Sent, int Withheld, int OverLimit);

/// <summary>
/// Decides without an AI model which research topics may leave the system. A topic goes out only when it holds no word of the files
/// or the About me details that is missing from the person's own request, and nothing unsafe (links, addresses, account or access details).
/// </summary>
/// <remarks>
/// Words are the longest runs of letters and digits after NFKC, in lower case; a word counts from three characters on or when it holds
/// a digit. Words are compared without accents, with "ß" as "ss", and also with "ä", "ö", "ü" written as "ae", "oe", "ue". A topic word
/// is withheld when it equals a word of the files or the profile, when one of the two starts with the other and the shorter has at least
/// <see cref="MinRelatedChars"/> characters (inflected forms and compound words), or when its digits, read across spaces, dots, commas,
/// slashes and hyphens, lie inside a number of the files or the profile or contain one of at least <see cref="MinNumberDigits"/> digits.
/// Only the same word in the request lets a topic word go (a number: a number of the request that holds the shared digits). Every rule
/// errs on the safe side: a withheld topic is assumed as before, a sent one cannot be taken back.
/// </remarks>
public static class ResearchQueryGate
{
    /// <summary>Shortest word that counts without a digit.</summary>
    public const int MinWordChars = 3;

    /// <summary>Shortest word that is compared by its start with longer words.</summary>
    public const int MinRelatedChars = 5;

    /// <summary>Fewest digits of a number that is compared across separators.</summary>
    public const int MinNumberDigits = 5;

    /// <summary>
    /// Selects at most <paramref name="max"/> topics in the order given, each on one line; repeated topics are skipped. A withheld topic
    /// takes no place of the limit.
    /// </summary>
    public static QuerySelection Select(IReadOnlyList<string> topics, string requestText, IReadOnlyList<string> fileTexts, string? profileText, int max)
    {
        var request = Vocabulary.From([requestText ?? string.Empty]);
        var foreign = Vocabulary.From([.. (fileTexts ?? []).Select(t => t ?? string.Empty), profileText ?? string.Empty]);
        var sent = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withheld = 0;
        var overLimit = 0;

        foreach (var raw in topics ?? [])
        {
            var topic = raw.CollapseWhiteSpace();
            if (topic.Length == 0 || !seen.Add(topic))
                continue;
            if (IntakeContentGuard.IsUnsafe(topic, requestText) || Reveals(topic, foreign, request))
            {
                withheld++;
                continue;
            }
            if (sent.Count >= max)
            {
                overLimit++;
                continue;
            }
            sent.Add(topic);
        }

        return new QuerySelection(sent, withheld, overLimit);
    }

    /// <summary>The counted words of <paramref name="text"/> as the gate reads them: NFKC, lower case, runs of letters and digits.</summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        foreach (var word in RawWords(text))
        {
            if (Counts(word))
                words.Add(word);
        }
        return words;
    }

    private static bool Reveals(string topic, Vocabulary foreign, Vocabulary request)
    {
        foreach (var word in Words(topic))
        {
            if (!request.Words.Contains(word) && Keys(word).Any(foreign.Relates))
                return true;
        }
        foreach (var number in NumbersIn(topic))
        {
            if (number.Length >= MinNumberDigits && foreign.Numbers.Any(n => Uncovered(number, n, request)))
                return true;
        }
        return false;
    }

    private static bool Uncovered(string number, string foreignNumber, Vocabulary request)
    {
        if (foreignNumber.Contains(number, StringComparison.Ordinal))
            return !request.Numbers.Any(n => n.Contains(number, StringComparison.Ordinal));
        if (foreignNumber.Length >= MinNumberDigits && number.Contains(foreignNumber, StringComparison.Ordinal))
            return !request.Numbers.Any(n => n.Contains(foreignNumber, StringComparison.Ordinal));
        return false;
    }

    private static bool Counts(string word) => word.Length >= MinWordChars || word.Any(char.IsDigit);

    private static IEnumerable<string> RawWords(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        var normalized = NormalizeKc(text);
        var builder = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || IsMark(rune))
            {
                if (builder.Length == 0 && IsMark(rune))
                    continue;
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
                continue;
            }
            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }
        if (builder.Length > 0)
            yield return builder.ToString();
    }

    private static IEnumerable<string> NumbersIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        var normalized = NormalizeKc(text);
        var digits = new StringBuilder();
        var pendingSeparator = false;
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (char.IsDigit(c))
            {
                digits.Append(c);
                pendingSeparator = false;
                continue;
            }
            if (digits.Length > 0 && !pendingSeparator && IsNumberSeparator(c) && i + 1 < normalized.Length && char.IsDigit(normalized[i + 1]))
            {
                pendingSeparator = true;
                continue;
            }
            if (digits.Length > 0)
            {
                yield return digits.ToString();
                digits.Clear();
            }
            pendingSeparator = false;
        }
        if (digits.Length > 0)
            yield return digits.ToString();
    }

    private static bool IsNumberSeparator(char c) => c is ' ' or '.' or ',' or '/' or '-' or ' ' or ' ';

    private static IEnumerable<string> Keys(string word)
    {
        var plain = Fold(word, transliterate: false);
        if (plain.Length > 0)
            yield return plain;
        var transliterated = Fold(word, transliterate: true);
        if (transliterated.Length > 0 && transliterated != plain)
            yield return transliterated;
    }

    private static string Fold(string word, bool transliterate)
    {
        var text = transliterate
            ? word.Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal).Replace("ü", "ue", StringComparison.Ordinal)
            : word;
        text = text.Replace("ß", "ss", StringComparison.Ordinal);
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            decomposed = text;
        }
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static string NormalizeKc(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private sealed class Vocabulary
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private string[] _sorted = [];

        public HashSet<string> Words { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Numbers { get; } = new(StringComparer.Ordinal);

        public static Vocabulary From(IReadOnlyList<string> texts)
        {
            var vocabulary = new Vocabulary();
            var words = vocabulary.Words;
            foreach (var text in texts)
            {
                foreach (var word in RawWords(text))
                {
                    if (Counts(word))
                        words.Add(word);
                }
                foreach (var number in NumbersIn(text))
                    vocabulary.Numbers.Add(number);
            }
            foreach (var word in words)
            {
                foreach (var key in Keys(word))
                    vocabulary._keys.Add(key);
            }
            vocabulary._sorted = [.. vocabulary._keys];
            Array.Sort(vocabulary._sorted, StringComparer.Ordinal);
            return vocabulary;
        }

        public bool Relates(string key)
        {
            if (_keys.Contains(key))
                return true;
            if (key.Length < MinRelatedChars)
                return false;
            for (var length = MinRelatedChars; length < key.Length; length++)
            {
                if (_keys.Contains(key[..length]))
                    return true;
            }
            return HasLongerStartingWith(key);
        }

        private bool HasLongerStartingWith(string key)
        {
            var index = Array.BinarySearch(_sorted, key, StringComparer.Ordinal);
            var next = index >= 0 ? index + 1 : ~index;
            return next < _sorted.Length && _sorted[next].StartsWith(key, StringComparison.Ordinal);
        }
    }
}
