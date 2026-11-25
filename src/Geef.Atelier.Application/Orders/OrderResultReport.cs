using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Application.Orders;

/// <summary>Fills the stage-3 fields of an <see cref="OrderResult"/> from the report of its run.</summary>
public static class OrderResultReport
{
    /// <summary>The fixed sentence for one writer's note that could not be shown.</summary>
    public const string NotesNotShownOne = "1 more note of the writer could not be shown because it holds a link, an address, or account or access details.";

    /// <summary>The fixed sentence for several writer's notes that could not be shown; <c>{0}</c> is their number.</summary>
    public const string NotesNotShownFormat = "{0} more notes of the writer could not be shown because they hold a link, an address, or account or access details.";

    /// <summary>
    /// Adds what <paramref name="report"/> says to <paramref name="result"/>. Schema 1: only <see cref="OrderResult.Checks"/>, which
    /// say that facts were not checked; the other new fields stay <c>null</c>. Schema 2: the writer's assumptions (without those
    /// the clarifier already listed), the writer's open points and a sentence for notes that could not be shown, the flagged
    /// passages and the contradictions in the material (both only when the fact check is shown, not in shadow mode), the
    /// sentences about the totals of the files, the verified quotes, the state and scope of the fact check and "What was
    /// checked". The state is never "checked" (nothing found) when flagged passages could not be listed. Passages from the About me
    /// details are shown only when <paramref name="showProfile"/> is true (the owner).
    /// </summary>
    public static OrderResult WithReport(this OrderResult result, RunReport report, bool showProfile = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(report);
        if (report.SchemaVersion < 2)
            return result with { Checks = OrderCheckLines.Build(report, result.HasOpenPoints()) };

        var factCheck = report.FactCheck;
        var shown = factCheck is { State: not (FactCheckState.Off or FactCheckState.NotShown) };
        var notes = report.WriterNotes ?? WriterNotes.Empty;
        var filled = result with
        {
            WriterAssumptions = WithoutClarifierAssumptions(notes.Assumptions, result),
            WriterOpenPoints = notes.OpenPoints.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
            NotesNotShown = notes.Hidden switch
            {
                <= 0 => null,
                1 => NotesNotShownOne,
                var n => string.Format(NotesNotShownFormat, n),
            },
            UnbackedPassages = shown
                ? factCheck!.Open
                    .Where(f => f.Kind != FactFindingKind.SourceConflict && !string.IsNullOrWhiteSpace(f.TextQuote))
                    .Select(f => new OrderUnbackedPassage(f.TextQuote!.Trim(), f.Kind,
                        f.Origin == InstructionOrigin.Memory && !showProfile ? null : f.InstructionQuote, f.Blocking, f.Origin))
                    .ToList()
                : [],
            SourceIssues = OrderCheckLines.SourceIssueSentences(report, showProfile),
            SourceQuotes = (report.SourceQuotes ?? []).Select(q => new OrderSourceQuoteView(q.FileName, q.Quote)).ToList(),
            FactCheck = factCheck is { State: FactCheckState.Checked, NotShown: > 0 } ? FactCheckState.OpenPoints : factCheck?.State,
            FactCheckScope = factCheck?.Scope,
        };
        return filled with { Checks = OrderCheckLines.Build(report, filled.HasOpenPoints()) };
    }

    private static IReadOnlyList<string> WithoutClarifierAssumptions(IReadOnlyList<string> writer, OrderResult result)
    {
        var known = (result.Assumptions ?? []).Select(a => Key(a.Text)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var assumption in writer.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            if (known.Add(Key(assumption)))
                kept.Add(assumption.Trim());
        }
        return kept;
    }

    private static string Key(string? text) => QuoteVerifier.Normalize(text ?? string.Empty).Trim().TrimEnd('.', '!', '?', ';', ':').Trim();
}
