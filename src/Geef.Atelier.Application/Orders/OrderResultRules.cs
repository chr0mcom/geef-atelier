using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Application.Orders;

/// <summary>The quotes shown for one file of a result, in the order of the page.</summary>
public sealed record OrderFileQuotes(string FileName, IReadOnlyList<string> Quotes);

/// <summary>
/// The rules by which the web page and MCP show a result, in one place so that both say the same: which flagged passages are
/// shown and in which order, which quotes per file, and whether there are open points at all.
/// </summary>
public static class OrderResultRules
{
    /// <summary>Quotes shown per file at most.</summary>
    public const int MaxQuotesPerFile = 3;

    /// <summary>Sentence shown with a passage of <see cref="FactHint.GapSentence"/>, on the web and in MCP.</summary>
    public const string GapSentenceNote = "This sentence has a gap to fill in. Please read the whole sentence when you fill it.";

    /// <summary>Sentence shown with a passage of <see cref="FactHint.GeneralClosing"/>, on the web and in MCP.</summary>
    public const string GeneralClosingNote = "A general closing sentence your request did not ask for. Please check that it fits.";

    /// <summary>Order of the blocking passages: not backed, against an instruction, replaced in the files.</summary>
    public static IReadOnlyList<FactFindingKind> BlockingPassageOrder { get; } =
        [FactFindingKind.Unsupported, FactFindingKind.Instruction, FactFindingKind.Superseded];

    /// <summary>
    /// The flagged passages that are shown: blocking ones grouped by <see cref="BlockingPassageOrder"/>, then the hints. Passages
    /// without a quote and contradictions between files (shown as sentences in <see cref="OrderResult.SourceIssues"/>) are left out.
    /// </summary>
    public static IReadOnlyList<OrderUnbackedPassage> ShownPassages(this OrderResult result) => ShownPassages(result.UnbackedPassages);

    /// <summary>The shown passages of <paramref name="passages"/> in the order of <see cref="ShownPassages(OrderResult)"/>.</summary>
    public static IReadOnlyList<OrderUnbackedPassage> ShownPassages(IReadOnlyList<OrderUnbackedPassage>? passages)
    {
        var shown = Candidates(passages);
        return BlockingPassageOrder
            .SelectMany(kind => shown.Where(p => p.Blocking && p.Kind == kind))
            .Concat(shown.Where(p => !p.Blocking))
            .ToList();
    }

    /// <summary>The shown blocking passages of <paramref name="kind"/>.</summary>
    public static IReadOnlyList<OrderUnbackedPassage> BlockingPassages(this OrderResult result, FactFindingKind kind) =>
        Candidates(result.UnbackedPassages).Where(p => p.Blocking && p.Kind == kind).ToList();

    /// <summary>The shown passages that never block (hints).</summary>
    public static IReadOnlyList<OrderUnbackedPassage> HintPassages(this OrderResult result) =>
        Candidates(result.UnbackedPassages).Where(p => !p.Blocking).ToList();

    /// <summary>
    /// The verified quotes per file: files in the order of <see cref="OrderResult.SourcesUsed"/> (also those without a quote), files
    /// that only appear in the quotes after them; per file at most <see cref="MaxQuotesPerFile"/> quotes, trimmed and without repeats.
    /// </summary>
    public static IReadOnlyList<OrderFileQuotes> QuotesByFile(this OrderResult result) =>
        QuotesByFile(result.SourcesUsed, result.SourceQuotes);

    /// <summary>The quotes per file as in <see cref="QuotesByFile(OrderResult)"/>, from the files used and the verified quotes.</summary>
    public static IReadOnlyList<OrderFileQuotes> QuotesByFile(IReadOnlyList<OrderSourceUsageView> sourcesUsed, IReadOnlyList<OrderSourceQuoteView>? sourceQuotes)
    {
        var quotes = (sourceQuotes ?? []).Where(q => !string.IsNullOrWhiteSpace(q.Quote)).ToList();
        return sourcesUsed.Select(s => s.FileName)
            .Concat(quotes.Select(q => q.FileName))
            .Distinct(StringComparer.Ordinal)
            .Select(file => new OrderFileQuotes(file, quotes
                .Where(q => string.Equals(q.FileName, file, StringComparison.Ordinal))
                .Select(q => q.Quote.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxQuotesPerFile)
                .ToList()))
            .ToList();
    }

    /// <summary>The non-blank lines of <paramref name="lines"/>; <c>null</c> stays <c>null</c>.</summary>
    public static IReadOnlyList<string>? Shown(this IReadOnlyList<string>? lines) =>
        lines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    /// <summary>
    /// The assumptions the text was written with: those of the clarifier nobody corrected, then those of the writer, without blank
    /// lines and without repeats (<see cref="SameAssumptionKey"/>; the first wording is kept). Assumptions about facts that went to
    /// the writer only as "not given by the person" (<see cref="NotGivenFacts"/>) are not among them.
    /// </summary>
    public static IReadOnlyList<string> AllAssumptions(this OrderResult result)
    {
        var notGiven = (result.NotGivenFacts ?? []).Select(AssumptionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (result.Assumptions ?? []).Select(a => a.Text)
            .Where(t => !notGiven.Contains(AssumptionKey(t)))
            .Concat(result.WriterAssumptions ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t) && seen.Add(SameAssumptionKey(t)))
            .ToList();
    }

    /// <summary>
    /// The key by which two assumptions count as the same and only the first is shown: the text normalized as quotes are (blanks,
    /// quote marks, dashes), without closing punctuation, compared without case, and without one leading article
    /// (<see cref="LeadingArticles"/>), so "Die Eltern werden mit Sie angesprochen." repeats "Eltern werden mit Sie angesprochen".
    /// Any other difference in wording keeps both, because only a person can tell whether they say the same.
    /// </summary>
    public static string SameAssumptionKey(string? text)
    {
        var key = QuoteVerifier.Normalize(text ?? string.Empty).Trim().TrimEnd('.', '!', '?', ';', ':', '…').Trim();
        var space = key.IndexOf(' ');
        return space > 0 && LeadingArticles.Contains(key[..space]) ? key[(space + 1)..] : key;
    }

    /// <summary>Articles that may stand before an assumption without changing what it says (German and English), compared without case.</summary>
    public static IReadOnlySet<string> LeadingArticles { get; } =
        new HashSet<string>(["der", "die", "das", "the"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the delivered text was written with passages looked up on the web: the result shows found entries, which
    /// <see cref="OrderResearchLines.Build"/> allows only when the delivered run had them in its material.
    /// </summary>
    public static bool UsedLookedUp(this OrderResult result) => result.LookedUp is { Status: ResearchStatus.Found, Entries.Count: > 0 };

    /// <summary>
    /// <paramref name="warnings"/> without the warning about research that did not work when <paramref name="lookedUp"/> already says
    /// so under "Looked up", so the person reads the sentence once; otherwise unchanged.
    /// </summary>
    public static IReadOnlyList<RunWarningEntity> WithoutShownResearchWarning(this IReadOnlyList<RunWarningEntity> warnings, OrderLookedUpView? lookedUp) =>
        lookedUp is { Status: ResearchStatus.Failed, Note: not null }
            ? warnings.Where(w => !string.Equals(w.Code, RunWarningCodes.ResearchFailed, StringComparison.Ordinal)).ToList()
            : warnings;

    /// <summary>The open points of the writer shown under "Still open": those not shown with the sentence about a total they repeat (<see cref="OrderResult.SourceIssueNotes"/>).</summary>
    public static IReadOnlyList<string> WriterOpenPointsOnTheirOwn(this OrderResult result)
    {
        var issues = (result.SourceIssues.Shown() ?? []).ToHashSet(StringComparer.Ordinal);
        var attached = (result.SourceIssueNotes ?? []).Where(n => issues.Contains(n.Issue)).Select(n => n.Note).ToHashSet(StringComparer.Ordinal);
        return (result.WriterOpenPoints.Shown() ?? []).Where(p => !attached.Contains(p)).ToList();
    }

    /// <summary>The open points of the writer shown with the sentence <paramref name="issue"/> of <see cref="OrderResult.SourceIssues"/>.</summary>
    public static IReadOnlyList<string> NotesOn(this OrderResult result, string issue) =>
        (result.SourceIssueNotes ?? []).Where(n => string.Equals(n.Issue, issue, StringComparison.Ordinal)).Select(n => n.Note).ToList();

    /// <summary>The assumptions of the clarifier about facts that the writer got only as "not given by the person", without blank lines.</summary>
    public static IReadOnlyList<string> NotGivenFacts(this OrderResult result) =>
        (result.NotGivenFacts ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

    /// <summary>True when "Open points" has something to show: placeholders, open checks, flagged passages, problems in the files, open points of the writer or notes that could not be shown.</summary>
    public static bool HasOpenPoints(this OrderResult result) =>
        result.Placeholders.Count > 0
        || result.OpenChecks.Count > 0
        || Candidates(result.UnbackedPassages).Count > 0
        || (result.SourceIssues.Shown()?.Count ?? 0) > 0
        || (result.WriterOpenPoints.Shown()?.Count ?? 0) > 0
        || !string.IsNullOrWhiteSpace(result.NotesNotShown);

    /// <summary>The key two assumption texts are compared by: one line, without surrounding blanks and closing punctuation.</summary>
    public static string AssumptionKey(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', '!', '?', ';', ':').Trim();

    private static IReadOnlyList<OrderUnbackedPassage> Candidates(IReadOnlyList<OrderUnbackedPassage>? passages) =>
        (passages ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Quote) && p.Kind != FactFindingKind.SourceConflict).ToList();
}
