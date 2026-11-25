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
/// already held by an earlier entry as whole words, over <see cref="MaxPerPage"/> per page, <see cref="MaxPerTopic"/> per topic or the
/// total. The entry is the passage of the raw text widened to whole words (<see cref="QuoteVerifier.ExcerptWholeWords"/>) with every
/// run of white space written as one space; <see cref="ResearchEntry.Start"/> and <see cref="ResearchEntry.Length"/> locate that passage
/// in the raw text.
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

        var entries = new List<ResearchEntry>();
        var dropped = new SortedDictionary<ResearchDropReason, int>();
        var perPage = new Dictionary<string, int>(StringComparer.Ordinal);
        var perTopic = new Dictionary<int, int>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var passage in proposed ?? [])
        {
            var reason = Decide(passage, byId, requestText, entries, perPage, perTopic, maxEntries, out var page, out var excerpt, out var match, out var start);
            if (reason is { } drop)
            {
                dropped[drop] = dropped.GetValueOrDefault(drop) + 1;
                continue;
            }

            perPage[page!.Id] = perPage.GetValueOrDefault(page.Id) + 1;
            perTopic[passage.Topic] = perTopic.GetValueOrDefault(passage.Topic) + 1;
            if (!hashes.TryGetValue(page.Id, out var hash))
                hashes[page.Id] = hash = page.RawText.Sha256Hex();
            entries.Add(new ResearchEntry($"{DossierRenderer.SourceId}.{entries.Count + 1}", passage.Topic, excerpt!.CollapseWhiteSpace(),
                page.Url ?? string.Empty, page.Title.CleanTitle(), retrievedAt, match, hash, start, excerpt!.Length));
        }

        return (entries, dropped);
    }

    private static ResearchDropReason? Decide(ProposedPassage? passage, Dictionary<string, ResearchPage> pages, string requestText,
        List<ResearchEntry> entries, Dictionary<string, int> perPage, Dictionary<int, int> perTopic, int maxEntries,
        out ResearchPage? page, out string? excerpt, out QuoteMatchKind match, out int start)
    {
        page = null;
        excerpt = null;
        match = QuoteMatchKind.NotFound;
        start = 0;

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
        excerpt = QuoteVerifier.ExcerptWholeWords(page.RawText, found);
        start = excerpt.Length == 0 ? -1 : page.RawText.IndexOf(excerpt, StringComparison.Ordinal);
        if (start < 0)
            return ResearchDropReason.NotInPage;
        match = found.Kind;

        if (excerpt.WordCount() < MinWords)
            return ResearchDropReason.TooShort;
        if (IntakeContentGuard.IsUnsafe(excerpt, requestText))
            return ResearchDropReason.Unsafe;
        var candidate = excerpt;
        if (entries.Any(e => QuoteVerifier.ContainsWholeWords(e.Quote, candidate)))
            return ResearchDropReason.Duplicate;
        if (perPage.GetValueOrDefault(page.Id) >= MaxPerPage || perTopic.GetValueOrDefault(passage.Topic) >= MaxPerTopic || entries.Count >= maxEntries)
            return ResearchDropReason.OverLimit;
        return null;
    }
}

/// <summary>Text helpers of the research.</summary>
internal static class ResearchTextExtensions
{
    /// <summary>Every run of white space as one space, without white space at the ends.</summary>
    public static string CollapseWhiteSpace(this string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
