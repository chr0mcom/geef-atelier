using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// What the delivered round of a direct-path run adds to its report (schema 2): the writer's notes, the files the notes quote,
/// the fact check round by round, the recalculated totals of the files and the codes of the checks without a model that failed
/// in round 1. Every field may be empty; an empty value never invents a check.
/// </summary>
internal sealed record DirectRunFacts(
    WriterNotesSplit? Notes = null,
    IReadOnlyList<PromptSource>? Sources = null,
    FactMaterial? Material = null,
    FactCheckMode? Mode = null,
    IReadOnlyList<FactCheckRound>? FactRounds = null,
    SourceArithmeticResult? Arithmetic = null,
    IReadOnlyList<string>? FirstRoundFailedChecks = null)
{
    /// <summary>No facts: the report of a run without stage-3 data.</summary>
    public static DirectRunFacts None { get; } = new();
}

/// <summary>Builds the report of a direct-path run from the delivered text, which is checked once more here.</summary>
internal static class DirectReportBuilder
{
    /// <summary>Report note when the input tokens suggest that the files were searched.</summary>
    public const string PartialReadNote = "Parts of your files were searched, not read in full.";

    /// <summary>Report note when the writer gave no list of its assumptions.</summary>
    public const string NotesAbsentNote = "The writer did not list assumptions.";

    /// <summary>
    /// Checks <paramref name="deliveredText"/> again (this also covers a best-effort version), lists its placeholders
    /// and the files used by the writer, and adds the read evidence of the first pass and, from <paramref name="facts"/>, the
    /// writer's notes with the quotes found word for word in their files, the fact check of the delivered round, the totals of
    /// the files and what was corrected after round 1. The passages looked up on the web (usage target
    /// <see cref="OrderGroundingStep.ResearchTarget"/>) follow the files as a source marked <see cref="RunReportSource.LookedUp"/>, and the
    /// report is marked <see cref="RunReport.LookedUp"/>; the writer's quotes from them are neither quotes from the files nor unverified.
    /// </summary>
    public static RunReport Build(
        OrderConstraints constraints,
        string deliveredText,
        IReadOnlyList<RunSourceUsageEntity> usages,
        int? deliveredIteration,
        int iterations,
        ReadEvidence? readEvidence = null,
        DirectRunFacts? facts = null)
    {
        facts ??= DirectRunFacts.None;
        var checks = DraftCheckEvaluator.Evaluate(deliveredText, constraints);
        var sources = usages
            .Where(u => u.Target == OrderGroundingStep.WriterTarget)
            .OrderBy(u => u.Position)
            .Select(u => new RunReportSource(u.SourceId, u.FileName, u.Mode, u.CharsIncluded))
            .Concat(usages
                .Where(u => u.Target == OrderGroundingStep.ResearchTarget)
                .OrderBy(u => u.Position)
                .Select(u => new RunReportSource(null, u.FileName, u.Mode, u.CharsIncluded, LookedUp: true)))
            .ToList();
        var lookedUp = sources.Any(s => s.LookedUp) || (facts.Sources ?? []).Any(s => s.LookedUp);

        var notes = new List<string>();
        if (readEvidence is { ReadInFull: false })
            notes.Add(PartialReadNote);

        var notesStatus = facts.Notes?.Status ?? WriterNotesStatus.Off;
        if (notesStatus == WriterNotesStatus.Absent)
            notes.Add(NotesAbsentNote);
        var writerNotes = notesStatus is WriterNotesStatus.Ok or WriterNotesStatus.CarriedOver ? facts.Notes!.Notes : WriterNotes.Empty;
        var (quotes, unverified) = VerifiedQuotes(writerNotes.Quotes, facts.Sources ?? [], facts.Material);
        unverified += writerNotes.HiddenQuotes;

        var factCheck = Summary(facts, deliveredIteration, deliveredText);
        var corrections = CheckCorrections(facts.FirstRoundFailedChecks, checks, deliveredIteration)
            .Concat((factCheck?.Corrected ?? []).Select(f => new RunReportCorrection(SourceFactCheckReviewer.CodeOf(f.Kind), f.TextQuote)))
            .ToList();

        return new RunReport(
            RunReport.CurrentSchemaVersion,
            RunReport.DirectKind,
            constraints,
            checks,
            DraftCheckEvaluator.FindPlaceholders(deliveredText),
            sources,
            deliveredIteration,
            iterations,
            readEvidence,
            notes,
            notesStatus,
            writerNotes,
            quotes,
            unverified,
            factCheck,
            facts.Arithmetic is { } arithmetic ? SourceArithmeticCheck.WithTextUse(arithmetic, deliveredText) : null,
            corrections,
            lookedUp);
    }

    /// <summary>
    /// The fact check of the delivered text. Off → <see cref="FactCheckState.Off"/>; shadow → <see cref="FactCheckState.NotShown"/>.
    /// Only a round whose check answered for exactly the delivered wording (<see cref="FactCheckRound.TextHash"/>) yields
    /// <see cref="FactCheckState.OpenPoints"/> (open passages, or flagged passages that could not be listed) or
    /// <see cref="FactCheckState.Checked"/> (nothing found, everything listed); its findings already hold those of earlier rounds that
    /// still apply, and when a later round was answered too (an earlier version was delivered as the best one), the findings of the
    /// later rounds are added the same way (<see cref="FactCarryOver.Merge"/>: source conflicts always, passages only when they stand
    /// in the delivered text) and the passages they could not list stay counted. A delivered round whose check failed yields <see cref="FactCheckState.NotCompleted"/>; a delivered text that no
    /// check saw (no round for it, or changed after its check) yields <see cref="FactCheckState.VersionNotChecked"/> when an earlier
    /// wording was checked, else <see cref="FactCheckState.NotCompleted"/>. Without a check of the delivered wording, findings of every
    /// answered round are kept when their passage still stands in the delivered text word for word (whole words), source conflicts
    /// always (<see cref="FactCarryOver.Merge"/>), and passages they could not list stay counted.
    /// <see cref="FactCheckSummary.Corrected"/> are the blocking findings of the answered rounds before the delivered one whose
    /// passage the delivered text no longer has. <c>null</c> without mode and rounds (a run before stage 3).
    /// </summary>
    internal static FactCheckSummary? Summary(DirectRunFacts facts, int? deliveredIteration, string? deliveredText = null)
    {
        var rounds = facts.FactRounds ?? [];
        if (facts.Mode is null && rounds.Count == 0)
            return null;

        var text = deliveredText ?? string.Empty;
        var hash = FactCheckRound.HashOf(text);
        var delivered = deliveredIteration is { } iteration ? rounds.LastOrDefault(r => r.Iteration == iteration) : rounds.LastOrDefault();
        var mode = delivered?.Mode ?? facts.Mode ?? rounds.LastOrDefault()?.Mode ?? FactCheckMode.Off;
        var answered = rounds.Where(r => r.Status == FactRoundStatus.Checked).ToList();
        var checkedThis = delivered is { Status: FactRoundStatus.Checked } && (delivered.TextHash is null || delivered.TextHash == hash);

        var later = checkedThis ? answered.Where(r => r.Iteration > delivered!.Iteration).OrderByDescending(r => r.Iteration).ToList() : [];
        var open = !checkedThis
            ? FactCarryOver.Merge([], answered.OrderByDescending(r => r.Iteration).Select(r => r.Open), text)
            : later.Count == 0 ? delivered!.Open : FactCarryOver.Merge(delivered!.Open, later.Select(r => r.Open), text);
        var notShown = checkedThis
            ? later.Select(r => r.NotShown).Append(delivered!.NotShown).Max()
            : answered.Select(r => r.NotShown).DefaultIfEmpty(0).Max();
        var passages = open.Where(f => f.Kind != FactFindingKind.SourceConflict).ToList();
        var conflicts = open.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList();

        var state = mode switch
        {
            FactCheckMode.Off => FactCheckState.Off,
            FactCheckMode.Shadow => FactCheckState.NotShown,
            _ when checkedThis => passages.Count > 0 || notShown > 0 ? FactCheckState.OpenPoints : FactCheckState.Checked,
            _ when delivered is { Status: FactRoundStatus.Failed } || answered.Count == 0 => FactCheckState.NotCompleted,
            _ => FactCheckState.VersionNotChecked,
        };

        var corrected = delivered is null
            ? []
            : answered.Where(r => r.Iteration < delivered.Iteration)
                .SelectMany(r => r.Open)
                .Where(f => f.Blocking && f.Kind != FactFindingKind.SourceConflict && !string.IsNullOrWhiteSpace(f.TextQuote)
                    && !QuoteVerifier.ContainsWholeWords(text, f.TextQuote))
                .DistinctBy(f => QuoteVerifier.Normalize(f.TextQuote!).Trim().ToLowerInvariant())
                .ToList();

        return new FactCheckSummary(
            mode,
            delivered?.Scope ?? answered.LastOrDefault()?.Scope ?? FactCheckScope.Full,
            state,
            passages,
            conflicts,
            corrected,
            rounds.Sum(r => r.Calls),
            delivered?.Model ?? rounds.LastOrDefault(r => r.Model is not null)?.Model,
            rounds.Any(r => r.UsedFallbackModel),
            FactCheckPrompts.Version,
            notShown);
    }

    /// <summary>Codes that failed and blocked in round 1 and pass in the delivered text (only when a later round was delivered).</summary>
    private static IEnumerable<RunReportCorrection> CheckCorrections(IReadOnlyList<string>? firstRound, IReadOnlyList<DraftCheckResult> delivered, int? deliveredIteration)
    {
        if (firstRound is null || deliveredIteration is not > 1)
            return [];
        var stillFailing = delivered.Where(c => !c.Passed).Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        return firstRound.Distinct(StringComparer.Ordinal)
            .Where(code => !stillFailing.Contains(code))
            .Select(code => new RunReportCorrection(code, null));
    }

    /// <summary>
    /// The writer's quotes found word for word (<see cref="QuoteMatch.IsVerbatim"/>) in the file they name, in the file's own wording; the rest
    /// is only counted. A quote from the passages looked up on the web is left out: it is no file of the person, and every passage stands in
    /// the research of the order anyway.
    /// </summary>
    private static (IReadOnlyList<RunReportQuote> Quotes, int Unverified) VerifiedQuotes(
        IReadOnlyList<WriterQuote> quotes, IReadOnlyList<PromptSource> sources, FactMaterial? material)
    {
        var verified = new List<RunReportQuote>();
        var unverified = 0;
        foreach (var quote in quotes)
        {
            var id = quote.SourceId?.Trim() ?? string.Empty;
            var source = sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var file = source is null ? material?.Files.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
            if (source is { LookedUp: true } || file is { LookedUp: true })
                continue;
            var text = source?.Text ?? file?.Text;
            var match = text is null || string.IsNullOrWhiteSpace(quote.Quote) ? null : QuoteVerifier.Find(text, quote.Quote.Trim(), allowApproximate: false);
            if (match is not { IsVerbatim: true })
            {
                unverified++;
                continue;
            }

            var excerpt = QuoteVerifier.Excerpt(text!, match);
            verified.Add(new RunReportQuote(source?.SourceId, source?.Id ?? file!.Id, source?.FileName ?? file!.FileName,
                excerpt.Length > 0 ? excerpt : quote.Quote.Trim(), match.Kind));
        }
        return (verified, unverified);
    }
}
