using System.Globalization;
using System.Text;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>Topics that may go to the search, and how many were withheld or over the limit.</summary>
public sealed record QuerySelection(IReadOnlyList<string> Sent, int Withheld, int OverLimit);

/// <summary>
/// Decides without an AI model which research topics may leave the system. A topic goes out only when it holds no word of the files
/// or the About me details that is missing from the person's own request, nothing unsafe (links, addresses, account or access details)
/// and no contact or account detail of a person (<see cref="ResearchTopicPrivacy.HoldsContactDetails"/>, also when the request names it).
/// </summary>
/// <remarks>
/// Texts are read after NFKC with format characters (soft hyphen, zero-width space and the like) removed. Words are the longest runs of
/// letters and digits, in lower case; a run written in camel case (<c>ArztbriefSchmidt</c>) or mixing letters and digits also gives its
/// parts. A word counts from three characters on or when it holds a digit. Words are compared without accents, with "ß" as "ss", and also
/// with "ä", "ö", "ü" written as "ae", "oe", "ue". A topic word is withheld when it, or one of its parts, equals a word of the files or the
/// profile, when one of the two starts with the other and the shorter has at least <see cref="MinRelatedChars"/> characters (inflected
/// forms and compound words), or when it is that word with a closing "s" (a genitive such as "Wolfs"). A number is read across spaces,
/// dots, commas, slashes and hyphens; it is compared as a number when it has at least <see cref="MinNumberDigits"/> digits or a dot or
/// comma between digits, also without closing zeros after such a mark (<c>1.250,00</c> as <c>1250</c>), and it is withheld when it
/// equals a number of the files or the profile, lies inside one or holds one of at least <see cref="MinNumberDigits"/> digits. Only the
/// same word in the request lets a topic word go (a number: a number of the request that holds the shared digits). With an order
/// language given, a topic whose own language is certainly another one (<see cref="TopicLanguage"/>) is withheld as well, because a
/// translated detail would pass the word rule. Every rule errs on the safe side: a withheld topic is assumed as before, a sent one
/// cannot be taken back. Translations without such a language signal, synonyms and the stem of a compound word ("Insolvenz" from
/// "Privatinsolvenz") are not recognized.
/// </remarks>
public static class ResearchQueryGate
{
    /// <summary>Shortest word that counts without a digit.</summary>
    public const int MinWordChars = 3;

    /// <summary>Shortest word that is compared by its start with longer words.</summary>
    public const int MinRelatedChars = 5;

    /// <summary>Fewest digits of a number without a dot or comma between digits that is compared as a number.</summary>
    public const int MinNumberDigits = 4;

    /// <summary>
    /// Selects at most <paramref name="max"/> topics in the order given, each on one line; repeated topics are skipped. A withheld topic
    /// takes no place of the limit.
    /// </summary>
    public static QuerySelection Select(IReadOnlyList<string> topics, string requestText, IReadOnlyList<string> fileTexts, string? profileText, int max) =>
        Select(topics, requestText, fileTexts, profileText, max, null);

    /// <summary>
    /// Like <see cref="Select(IReadOnlyList{string}, string, IReadOnlyList{string}, string?, int)"/>; with <paramref name="orderLanguage"/>
    /// (<c>de</c>, <c>en</c> or another code) a topic whose language is certainly another one is withheld too.
    /// </summary>
    public static QuerySelection Select(IReadOnlyList<string> topics, string requestText, IReadOnlyList<string> fileTexts, string? profileText, int max,
        string? orderLanguage)
    {
        var language = BaseLanguage(orderLanguage);
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
            if (IntakeContentGuard.IsUnsafe(topic, requestText) || topic.HoldsContactDetails() || Reveals(topic, foreign, request) || OtherLanguage(topic, language))
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

    /// <summary>
    /// The counted words of <paramref name="text"/> as the gate reads them: NFKC without format characters, lower case, runs of letters
    /// and digits, each followed by its camel-case and letter-digit parts.
    /// </summary>
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

    /// <summary>
    /// The language of a short topic when it is certain: <c>de</c> when it holds German function words or "ä", "ö", "ü", "ß" and no
    /// English function word, <c>en</c> when it holds English function words and no German signal (<see cref="Checks.TextLanguageGuess.Count"/>);
    /// otherwise <c>null</c>.
    /// </summary>
    public static string? TopicLanguage(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return null;
        var (german, english) = Checks.TextLanguageGuess.Count(topic);
        var germanLetters = topic.Any(c => c is 'ä' or 'ö' or 'ü' or 'ß' or 'Ä' or 'Ö' or 'Ü');
        if ((german > 0 || germanLetters) && english == 0)
            return "de";
        if (english > 0 && german == 0 && !germanLetters)
            return "en";
        return null;
    }

    private static string? BaseLanguage(string? language)
    {
        var trimmed = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        var dash = trimmed.IndexOfAny(['-', '_']);
        return dash > 0 ? trimmed[..dash] : trimmed;
    }

    private static bool OtherLanguage(string topic, string? orderLanguage) =>
        orderLanguage is not null && TopicLanguage(topic) is { } own && own != orderLanguage;

    private static bool Reveals(string topic, Vocabulary foreign, Vocabulary request)
    {
        foreach (var word in Words(topic))
        {
            if (request.Words.Contains(word))
                continue;
            if (Keys(word).Any(foreign.Relates))
                return true;
            if (word.Length > MinWordChars && word[^1] == 's' && Keys(word[..^1]).Any(foreign.Contains))
                return true;
        }
        foreach (var number in NumbersIn(topic))
        {
            if (number.IsCompared && foreign.Numbers.Any(n => Uncovered(number, n, request)))
                return true;
        }
        return false;
    }

    private static bool Uncovered(Number number, Number foreignNumber, Vocabulary request)
    {
        foreach (var digits in number.Forms)
        {
            foreach (var foreignDigits in foreignNumber.Forms)
            {
                var shared = foreignDigits == digits
                    || (digits.Length >= MinNumberDigits && foreignDigits.Contains(digits, StringComparison.Ordinal))
                    ? digits
                    : foreignNumber.IsCompared && foreignDigits.Length >= MinNumberDigits && digits.Contains(foreignDigits, StringComparison.Ordinal)
                        ? foreignDigits
                        : null;
                if (shared is not null && !request.Numbers.Any(n => n.Forms.Any(f => f.Contains(shared, StringComparison.Ordinal))))
                    return true;
            }
        }
        return false;
    }

    private static bool Counts(string word) => word.Length >= MinWordChars || word.Any(char.IsDigit);

    private static IEnumerable<string> RawWords(string? text)
    {
        foreach (var run in Runs(text))
        {
            yield return run.ToLowerInvariant();
            var parts = Parts(run);
            if (parts.Count < 2)
                continue;
            foreach (var part in parts)
                yield return part.ToLowerInvariant();
        }
    }

    private static IEnumerable<string> Runs(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        var builder = new StringBuilder();
        foreach (var rune in Prepare(text).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || IsMark(rune))
            {
                if (builder.Length == 0 && IsMark(rune))
                    continue;
                builder.Append(rune.ToString());
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

    private static List<string> Parts(string run)
    {
        var runes = run.EnumerateRunes().ToList();
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (current.Length > 0 && !IsMark(rune) && i > 0 && Splits(runes, i))
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            current.Append(rune.ToString());
        }
        if (current.Length > 0)
            parts.Add(current.ToString());
        return parts;
    }

    private static bool Splits(List<Rune> runes, int i)
    {
        var previous = runes[i - 1];
        var rune = runes[i];
        if (IsMark(previous))
            return false;
        if (Rune.IsLetter(previous) && Rune.IsDigit(rune) || Rune.IsDigit(previous) && Rune.IsLetter(rune))
            return true;
        if (Rune.IsLower(previous) && Rune.IsUpper(rune))
            return true;
        return Rune.IsUpper(previous) && Rune.IsUpper(rune) && i + 1 < runes.Count && Rune.IsLower(runes[i + 1]);
    }

    private static IEnumerable<Number> NumbersIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        var normalized = Prepare(text);
        var digits = new StringBuilder();
        var pendingSeparator = false;
        var marked = false;
        var lastMark = -1;
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
                if (c is '.' or ',')
                {
                    marked = true;
                    lastMark = digits.Length;
                }
                continue;
            }
            if (digits.Length > 0)
            {
                yield return Number.Of(digits.ToString(), marked, lastMark);
                digits.Clear();
            }
            pendingSeparator = false;
            marked = false;
            lastMark = -1;
        }
        if (digits.Length > 0)
            yield return Number.Of(digits.ToString(), marked, lastMark);
    }

    private static bool IsNumberSeparator(char c) => c is ' ' or '.' or ',' or '/' or '-' or '\'' or '\u2019' or '\u02BC' or '\u2032' or '\u066C'
        or '\u00A0' or '\u202F' or '\u2009' or '\u2007' or '\u2008';

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

    private static string Prepare(string text)
    {
        string folded;
        try
        {
            folded = text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            folded = text;
        }
        var builder = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private sealed class Vocabulary
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private string[] _sorted = [];

        public HashSet<string> Words { get; } = new(StringComparer.Ordinal);

        public List<Number> Numbers { get; } = [];

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

        public bool Contains(string key) => _keys.Contains(key);

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

    private sealed record Number(string Digits, string? WithoutZeros, bool Marked)
    {
        public bool IsCompared => Digits.Length >= MinNumberDigits || Marked;

        public IEnumerable<string> Forms => WithoutZeros is null ? [Digits] : [Digits, WithoutZeros];

        public static Number Of(string digits, bool marked, int lastMark)
        {
            var withoutZeros = marked && lastMark > 0 && lastMark < digits.Length && digits[lastMark..].All(c => c == '0') ? digits[..lastMark] : null;
            return new Number(digits, withoutZeros, marked);
        }
    }
}
