using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>
/// Turns proposed passages into entries without an AI model: every passage must stand word for word in the raw text of its page, and the
/// entry keeps the wording of the page, not the proposal. Each dropped passage is counted with its <see cref="ResearchDropReason"/>.
/// </summary>
/// <remarks>
/// Rules per proposal, the first that applies decides: unknown page (<see cref="ResearchDropReason.UnknownPage"/>), longer than
/// <see cref="MaxQuoteChars"/> (<see cref="ResearchDropReason.OverlongQuote"/>, never shortened), not word for word in the page by
/// <see cref="QuoteVerifier.FindVerbatim"/> (<see cref="ResearchDropReason.NotInPage"/>; a quote too short to look up counts as
/// <see cref="ResearchDropReason.TooShort"/>), fewer than <see cref="MinWords"/> words, unsafe by <see cref="IntakeContentGuard.IsUnsafe"/>,
/// already held by an earlier entry as whole words (an earlier entry that the new passage holds as whole words gives way to it and
/// counts as <see cref="ResearchDropReason.Duplicate"/>), over <see cref="MaxPerPage"/> per page, <see cref="MaxPerTopic"/> per topic or the
/// total. The entry is the passage of the raw text widened to whole words (<see cref="QuoteVerifier.ExcerptWholeWords"/>) and to whole
/// numbers (a passage never starts or ends inside a number such as <c>1,5</c> or <c>1.250</c>) with every
/// run of white space written as one space; <see cref="ResearchEntry.Start"/> and <see cref="ResearchEntry.Length"/> locate that passage
/// in the raw text. The title of an entry is <see cref="ResearchAddress.SafeTitle"/>.
/// </remarks>
public static class DossierBuilder
{
    public const int MinWords = 5;
    public const int MaxQuoteChars = 600;
    public const int MaxPerPage = 3;
    public const int MaxPerTopic = 4;

    /// <summary>Longest page title kept in an entry.</summary>
    public const int MaxTitleChars = 200;

    /// <summary>Builds the entries <c>W1.1…</c> in the order of <paramref name="proposed"/>; only reasons that occurred are in the counts.</summary>
    public static (IReadOnlyList<ResearchEntry> Entries, IReadOnlyDictionary<ResearchDropReason, int> Dropped) Build(
        IReadOnlyList<ProposedPassage> proposed, IReadOnlyList<ResearchPage> pages, string requestText, int maxEntries, DateTimeOffset retrievedAt)
    {
        var byId = new Dictionary<string, ResearchPage>(StringComparer.Ordinal);
        foreach (var page in pages ?? [])
        {
            if (page is not null && page.Id is not null)
                byId.TryAdd(page.Id, page);
        }

        var entries = new List<Kept>();
        var dropped = new SortedDictionary<ResearchDropReason, int>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var passage in proposed ?? [])
        {
            var reason = Decide(passage, byId, requestText, entries, maxEntries, out var page, out var excerpt, out var match, out var start, out var replaced);
            if (reason is { } drop)
            {
                dropped[drop] = dropped.GetValueOrDefault(drop) + 1;
                continue;
            }

            if (!hashes.TryGetValue(page!.Id, out var hash))
                hashes[page.Id] = hash = page.RawText.Sha256Hex();
            var entry = new Kept(page.Id, new ResearchEntry(string.Empty, passage.Topic, excerpt!.CollapseWhiteSpace(),
                page.Url ?? string.Empty, ResearchAddress.SafeTitle(page.Title, page.Url), retrievedAt, match, hash, start, excerpt!.Length));
            if (replaced.Count == 0)
            {
                entries.Add(entry);
                continue;
            }

            var at = entries.IndexOf(replaced[0]);
            foreach (var old in replaced)
                entries.Remove(old);
            entries.Insert(Math.Min(at, entries.Count), entry);
            dropped[ResearchDropReason.Duplicate] = dropped.GetValueOrDefault(ResearchDropReason.Duplicate) + replaced.Count;
        }

        return (entries.Select((e, i) => e.Entry with { Id = $"{DossierRenderer.SourceId}.{i + 1}" }).ToList(), dropped);
    }

    private static ResearchDropReason? Decide(ProposedPassage? passage, Dictionary<string, ResearchPage> pages, string requestText,
        List<Kept> entries, int maxEntries, out ResearchPage? page, out string? excerpt, out QuoteMatchKind match, out int start,
        out IReadOnlyList<Kept> replaced)
    {
        page = null;
        excerpt = null;
        match = QuoteMatchKind.NotFound;
        start = 0;
        replaced = [];

        if (passage?.PageId is null || !pages.TryGetValue(passage.PageId, out page) || page.RawText is null)
            return ResearchDropReason.UnknownPage;
        var quote = passage.Quote ?? string.Empty;
        if (quote.Length > MaxQuoteChars)
            return ResearchDropReason.OverlongQuote;

        var found = QuoteVerifier.FindVerbatim(page.RawText, quote, QuoteVerifier.MinChars);
        if (found.Kind == QuoteMatchKind.TooShort)
            return ResearchDropReason.TooShort;
        if (!found.IsVerbatim)
            return ResearchDropReason.NotInPage;
        var words = QuoteVerifier.ExcerptWholeWords(page.RawText, found);
        start = words.Length == 0 ? -1 : page.RawText.IndexOf(words, StringComparison.Ordinal);
        if (start < 0)
            return ResearchDropReason.NotInPage;
        (start, var end) = page.RawText.WholeNumbers(start, start + words.Length);
        excerpt = page.RawText[start..end];
        match = found.Kind;

        if (excerpt.WordCount() < MinWords)
            return ResearchDropReason.TooShort;
        if (IntakeContentGuard.IsUnsafe(excerpt, requestText))
            return ResearchDropReason.Unsafe;
        var candidate = excerpt;
        if (entries.Any(e => QuoteVerifier.ContainsWholeWords(e.Entry.Quote, candidate)))
            return ResearchDropReason.Duplicate;
        var held = entries.Where(e => QuoteVerifier.ContainsWholeWords(candidate, e.Entry.Quote)).ToList();
        var kept = entries.Except(held).ToList();
        var pageId = page.Id;
        if (kept.Count(e => e.PageId == pageId) >= MaxPerPage
            || kept.Count(e => e.Entry.Topic == passage.Topic) >= MaxPerTopic
            || kept.Count >= maxEntries)
            return ResearchDropReason.OverLimit;
        replaced = held;
        return null;
    }

    private sealed record Kept(string PageId, ResearchEntry Entry);
}

/// <summary>Text helpers of the research.</summary>
internal static class ResearchTextExtensions
{
    /// <summary>Every run of white space as one space, without white space at the ends.</summary>
    public static string CollapseWhiteSpace(this string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The range <paramref name="start"/>–<paramref name="end"/> of <paramref name="text"/> widened so that it never starts or ends inside
    /// a number written with a dot or a comma between digits (<c>1,5</c>, <c>1.250,00</c>) or with a space or an apostrophe before a
    /// group of exactly three digits (<c>1 500</c>, <c>4'850</c>, <c>1’250’000</c>): such a number is taken whole.
    /// </summary>
    public static (int Start, int End) WholeNumbers(this string text, int start, int end)
    {
        while (start > 0)
        {
            if ((char.IsDigit(text[start]) && (char.IsDigit(text[start - 1]) || text.JoinsDigits(start - 1))) || text.JoinsDigits(start))
                start--;
            else
                break;
        }
        while (end < text.Length)
        {
            if ((char.IsDigit(text[end]) && (char.IsDigit(text[end - 1]) || text.JoinsDigits(end - 1))) || text.JoinsDigits(end))
                end++;
            else
                break;
        }
        return (start, end);
    }

    private static bool JoinsDigits(this string text, int index) =>
        index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1])
        && (text[index] is '.' or ',' || (text[index].IsGroupSeparator() && text.DigitsBefore(index) <= 3 && text.DigitsAfter(index) == 3));

    private static bool IsGroupSeparator(this char c) =>
        c is ' ' or '\'' or '\u2019' or '\u02BC' or '\u00A0' or '\u202F' or '\u2009' or '\u2007' or '\u2008';

    private static int DigitsBefore(this string text, int index)
    {
        var count = 0;
        while (index - 1 - count >= 0 && char.IsDigit(text[index - 1 - count]))
            count++;
        return count;
    }

    private static int DigitsAfter(this string text, int index)
    {
        var count = 0;
        while (index + 1 + count < text.Length && char.IsDigit(text[index + 1 + count]))
            count++;
        return count;
    }

    /// <summary>Number of runs of letters and digits.</summary>
    public static int WordCount(this string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var part = Rune.IsLetterOrDigit(rune);
            if (part && !inWord)
                count++;
            inWord = part;
        }
        return count;
    }

    /// <summary>The title on one line, without control or format characters, at most <see cref="DossierBuilder.MaxTitleChars"/> characters.</summary>
    public static string CleanTitle(this string? title)
    {
        var builder = new StringBuilder();
        foreach (var c in title ?? string.Empty)
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsWhiteSpace(c) || category is UnicodeCategory.Control)
                builder.Append(' ');
            else if (category is not UnicodeCategory.Format)
                builder.Append(c);
        }
        var line = builder.ToString().CollapseWhiteSpace();
        if (line.Length <= DossierBuilder.MaxTitleChars)
            return line;
        var cut = DossierBuilder.MaxTitleChars - 1;
        if (char.IsHighSurrogate(line[cut - 1]))
            cut--;
        return line[..cut].TrimEnd() + "…";
    }

    /// <summary>SHA-256 of the UTF-8 bytes of <paramref name="text"/>, lower-case hex.</summary>
    public static string Sha256Hex(this string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
