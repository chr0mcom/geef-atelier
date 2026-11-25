using System.Globalization;
using Geef.Atelier.Application.Orders;
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
/// in round 1. <see cref="Revision"/> is set only for a run that changes an earlier version of the order. Every field may be empty; an
/// empty value never invents a check.
/// </summary>
internal sealed record DirectRunFacts(
    WriterNotesSplit? Notes = null,
    IReadOnlyList<PromptSource>? Sources = null,
    FactMaterial? Material = null,
    FactCheckMode? Mode = null,
    IReadOnlyList<FactCheckRound>? FactRounds = null,
    SourceArithmeticResult? Arithmetic = null,
    IReadOnlyList<string>? FirstRoundFailedChecks = null,
    DirectRevision? Revision = null)
{
    /// <summary>No facts: the report of a run without stage-3 data.</summary>
    public static DirectRunFacts None { get; } = new();
}

/// <summary>
/// The earlier version a run changes: its <see cref="Version"/> in the order (<c>null</c> when the run of that version has none or is gone)
/// and its stored report (<c>null</c> when it has none). <see cref="ReportUnreadable"/> says that a stored report could not be read,
/// <see cref="NeedsCheck"/> that the earlier version itself was shown as "Ready – please check".
/// </summary>
internal sealed record DirectRevision(int? Version, RunReport? Report, bool ReportUnreadable = false, bool NeedsCheck = false);

/// <summary>Builds the report of a direct-path run from the delivered text, which is checked once more here.</summary>
internal static class DirectReportBuilder
{
    /// <summary>Report note when the input tokens suggest that the files were searched.</summary>
    public const string PartialReadNote = "Parts of your files were searched, not read in full.";

    /// <summary>Report note when the writer gave no list of its assumptions.</summary>
    public const string NotesAbsentNote = "The writer did not list assumptions.";

    /// <summary>Report note when the run changes an earlier version whose report could not be read, so its open points are not repeated.</summary>
    public const string EarlierReportMissingNote = "The open points of the earlier version could not be read, so they are not repeated here.";

    /// <summary>
    /// Open point for the earlier version when it has no report (for example a text written with "Extra careful"): <c>{0}</c> is that
    /// version. Its checks were not taken over and the new run does not repeat them.
    /// </summary>
    public const string ChecksNotTakenOverFormat = "The checks of version {0} were not taken over, so anything they left open was not checked again.";

    /// <summary>
    /// Checks <paramref name="deliveredText"/> again (this also covers a best-effort version), lists its placeholders
    /// and the files used by the writer, and adds the read evidence of the first pass and, from <paramref name="facts"/>, the
    /// writer's notes with the quotes found word for word in their files, the fact check of the delivered round, the totals of
    /// the files and what was corrected after round 1. The passages looked up on the web (usage target
    /// <see cref="OrderGroundingStep.ResearchTarget"/>) follow the files as a source marked <see cref="RunReportSource.LookedUp"/>, and the
    /// report is marked <see cref="RunReport.LookedUp"/>; the writer's quotes from them are neither quotes from the files nor unverified.
    /// For a run that changes an earlier version (<see cref="DirectRunFacts.Revision"/>) the length the first request asked for is only a
    /// note (<see cref="DeterministicChecksReviewer.Evaluate"/>), the open findings of the earlier version that still apply are kept
    /// (<see cref="Summary"/>) and the writer's open points of the earlier version that this run does not repeat become
    /// <see cref="RunReport.EarlierOpenPoints"/> (<see cref="EarlierPoints"/>); nothing known is dropped.
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
        var checks = DeterministicChecksReviewer.Evaluate(deliveredText, constraints, facts.Revision is not null);
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
        if (facts.Revision is { ReportUnreadable: true })
            notes.Add(EarlierReportMissingNote);
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
            lookedUp,
            EarlierPoints(facts.Revision, writerNotes.OpenPoints, writerNotes.Assumptions),
            facts.Revision is not null,
            EarlierNeedsCheck(facts.Revision));
    }

    /// <summary>
    /// True when the earlier version (<paramref name="revision"/>) was shown as "Ready – please check" and its checks were not taken over
    /// because it has no readable report (for example a text written with "Extra careful" that was not finished cleanly), or when its own
    /// report says so of the version before it; then nothing of it was checked again and this version is shown the same way, in every
    /// mode of the fact check (<see cref="RunReport.EarlierNeedsCheck"/>).
    /// </summary>
    internal static bool EarlierNeedsCheck(DirectRevision? revision) =>
        revision is { Report: null, NeedsCheck: true } || revision?.Report is { EarlierNeedsCheck: true };

    /// <summary>
    /// What the writer noted for the earlier version (<paramref name="revision"/>) that this run does not repeat in the same kind: first
    /// what the earlier version itself carried over (each with the version that noted it), then its own open points and its own assumptions
    /// (<see cref="EarlierPointKind.Assumption"/>), each with its version, and a fixed sentence (<see cref="EarlierPointKind.Note"/>) for its
    /// notes and flagged passages that could not be shown or listed. A point counts as repeated when this run notes it in the same kind word
    /// for word (<see cref="OrderResultRules.SameAssumptionKey"/>); each point is carried once, a fixed sentence once per version. When the
    /// earlier version has no report (it was not written on the direct path, for example with "Extra careful"),
    /// <see cref="ChecksNotTakenOverFormat"/> says that its checks were not taken over. <c>null</c> when
    /// there is nothing or the run changes no earlier version, so that the report of every other run stays as it was.
    /// </summary>
    internal static IReadOnlyList<RunReportEarlierPoint>? EarlierPoints(DirectRevision? revision, IReadOnlyList<string> openPoints,
        IReadOnlyList<string>? assumptions = null)
    {
        if (revision is not { Version: { } version })
            return null;

        static HashSet<string> Keys(IEnumerable<string>? texts) =>
            (texts ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(OrderResultRules.SameAssumptionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repeatedOpen = Keys(openPoints);
        var repeatedAssumed = Keys(assumptions);
        var carried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var points = new List<RunReportEarlierPoint>();
        void Add(RunReportEarlierPoint point)
        {
            if (string.IsNullOrWhiteSpace(point.Text))
                return;
            var key = OrderResultRules.SameAssumptionKey(point.Text);
            var repeated = point.Kind == EarlierPointKind.Assumption ? repeatedAssumed : repeatedOpen;
            if (!repeated.Contains(key) && carried.Add(point.Kind == EarlierPointKind.Note ? $"{point.Kind}|{point.Version}|{key}" : $"{point.Kind}|{key}"))
                points.Add(point with { Text = point.Text.Trim() });
        }

        if (revision.Report is not { } earlier)
        {
            if (!revision.ReportUnreadable)
                Add(new RunReportEarlierPoint(version, string.Format(CultureInfo.InvariantCulture, ChecksNotTakenOverFormat, version), EarlierPointKind.Note));
            return points.Count == 0 ? null : points;
        }

        var notes = earlier.WriterNotes ?? WriterNotes.Empty;
        foreach (var point in earlier.EarlierOpenPoints ?? [])
            Add(point);
        foreach (var point in notes.OpenPoints ?? [])
            Add(new RunReportEarlierPoint(version, point));
        foreach (var assumption in notes.Assumptions ?? [])
            Add(new RunReportEarlierPoint(version, assumption, EarlierPointKind.Assumption));
        if (notes.Hidden > 0)
            Add(new RunReportEarlierPoint(version, notes.Hidden == 1
                ? OrderResultReport.NotesNotShownOne
                : string.Format(CultureInfo.InvariantCulture, OrderResultReport.NotesNotShownFormat, notes.Hidden), EarlierPointKind.Note));
        if (earlier.FactCheck is { State: not (FactCheckState.Off or FactCheckState.NotShown), NotShown: > 0 and var notShown })
            Add(new RunReportEarlierPoint(version, (notShown == 1
                ? OrderCheckLines.FactsNotShownOnlyOne
                : string.Format(CultureInfo.InvariantCulture, OrderCheckLines.FactsNotShownOnlyFormat, notShown)).Trim(), EarlierPointKind.Note));
        return points.Count == 0 ? null : points;
    }

    /// <summary>
    /// The open findings of the earlier version (<paramref name="revision"/>), passages and source conflicts, each marked with the version
    /// that found it (<see cref="FactFinding.FromVersion"/>; a finding the earlier version carried over keeps its own). A finding the earlier
    /// version carried over is taken also when its own fact check was off or not shown, so that it is never lost over versions; the earlier
    /// version's own findings only when its fact check was shown (findings of the shadow mode stay unseen). A finding against an instruction
    /// blocks only when its instruction stands in the newest change of <paramref name="material"/> (<see cref="FactFindingFilter.ForRevision"/>),
    /// so one from before the latest change never blocks again in a later version. Empty when the run changes no earlier version.
    /// </summary>
    internal static IReadOnlyList<FactFinding> EarlierFindings(DirectRevision? revision, FactMaterial? material = null)
    {
        if (revision is not { Version: { } version, Report.FactCheck: { } earlier })
            return [];
        var shown = earlier.State is not (FactCheckState.Off or FactCheckState.NotShown);
        return (earlier.Open ?? []).Concat(earlier.SourceConflicts ?? [])
            .Where(f => f is not null && (shown || f.FromVersion is not null))
            .Select(f => FactFindingFilter.ForRevision(f with { FromVersion = f.FromVersion ?? version }, material))
            .ToList();
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
    /// passage the delivered text no longer has. In a run that changes an earlier version, the open findings of that version
    /// (<see cref="EarlierFindings"/>) are added last the same way (<see cref="FactCarryOver.Merge"/>: source conflicts always, passages only
    /// when they stand in the delivered text), marked with their version; a finding this run reports itself is shown once, as its own. When
    /// this run's check is off or in shadow mode, the findings of the earlier version come first, so that a finding of the shadow mode never
    /// hides one carried over.
    /// <c>null</c> without mode and rounds (a run before stage 3).
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
        if (EarlierFindings(facts.Revision, facts.Material) is { Count: > 0 } earlier)
            open = mode is FactCheckMode.Off or FactCheckMode.Shadow
                ? FactCarryOver.Merge(FactCarryOver.Merge([], [earlier], text), [open], text)
                : FactCarryOver.Merge(open, [earlier], text);
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
