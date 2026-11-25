using Geef.Atelier.Core.Domain.FactCheck;

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
    /// lines. Assumptions about facts that went to the writer only as "not given by the person" (<see cref="NotGivenFacts"/>) are
    /// not among them.
    /// </summary>
    public static IReadOnlyList<string> AllAssumptions(this OrderResult result)
    {
        var notGiven = (result.NotGivenFacts ?? []).Select(AssumptionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (result.Assumptions ?? []).Select(a => a.Text)
            .Where(t => !notGiven.Contains(AssumptionKey(t)))
            .Concat(result.WriterAssumptions ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

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
