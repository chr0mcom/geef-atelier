using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>Outcome of V1 after the decision table of contract stage 3, 3.1.</summary>
public enum ProbeOutcome
{
    /// <summary>Block as planned, <c>Direct:WriterNotes = true</c>.</summary>
    A,

    /// <summary>No damage, but long texts often without block: block stays, <c>Absent</c> is reported.</summary>
    B,

    /// <summary>Damage or short texts below 8 of 10: no block, package R2b (extra call).</summary>
    C,

    /// <summary>Not enough data for a decision; the detail names the next stage.</summary>
    NotEvaluable,
}

/// <summary>Figures of one size (short or long) over every attempt.</summary>
public sealed record ProbeFigures(string Size, int Attempts, int Ok, int Absent, int Unreadable, int NoAnswer, int Damaged, IReadOnlyList<string> DamageDetail, double MedianWordShare, double P90Ms)
{
    /// <summary>Share of attempts whose block was read (<c>Ok</c>); failed attempts are in the denominator.</summary>
    public double OkRate => Attempts == 0 ? 0 : (double)Ok / Attempts;

    /// <summary>One line for the console.</summary>
    public override string ToString() =>
        $"{Size}: {Ok} of {Attempts} Ok, {Absent} absent, {Unreadable} unreadable, {NoAnswer} without answer, {Damaged} damaged" +
        $"{(DamageDetail.Count > 0 ? $" ({string.Join("; ", DamageDetail)})" : string.Empty)}, median length {MedianWordShare:P0} of the target, p90 {P90Ms / 1000:0} s";
}

/// <summary>
/// Judges V1 from the stored writer answers, re-split with the current <c>WriterNotesFormat.Split</c>. Every attempt counts:
/// a failed or timed-out attempt lowers the share of <c>Ok</c>, and one damaged answer is enough for outcome C. Results for
/// another input (prompt, files, model) or unknown orders make the file not evaluable.
/// </summary>
public static class NotesProbeEvaluation
{
    /// <summary>Short attempts of the decision (contract: 10).</summary>
    public const int ShortAttempts = 10;

    /// <summary>Long attempts after an error-free stage b (contract: 5).</summary>
    public const int LongAttemptsClean = 5;

    /// <summary>Long attempts after any error (contract: 10).</summary>
    public const int LongAttemptsFull = 10;

    /// <summary>Lowest share of <c>Ok</c> for short texts for outcome A (9 of 10).</summary>
    public const double ShortOkForA = 0.9;

    /// <summary>Below this share of <c>Ok</c> for short texts the outcome is C (8 of 10).</summary>
    public const double ShortOkForNotC = 0.8;

    /// <summary>Lowest share of <c>Ok</c> for long texts for outcome A (4 of 5, 8 of 10).</summary>
    public const double LongOkForA = 0.8;

    /// <summary>Re-reads every line from its raw answer with <paramref name="split"/> (lines without answer keep their status).</summary>
    public static IReadOnlyList<ProbeLine> Reread(IReadOnlyList<ProbeLine> lines, Func<string, WriterNotesSplit> split) =>
        lines.Select(l =>
        {
            if (l.Raw is null)
                return l;
            var reading = NotesProbe.Read(l.Raw, l.FinishReason, split);
            return l with { Status = reading.Status, Damage = reading.Damage, Words = reading.Words, LastLine = reading.LastLine };
        }).ToList();

    /// <summary>Figures of one size.</summary>
    public static ProbeFigures Figures(string size, IReadOnlyList<ProbeLine> lines)
    {
        var part = lines.Where(l => l.Size == size).ToList();
        var answered = part.Where(l => l.Raw is not null).ToList();
        var shares = answered.Where(l => l.TargetWords > 0).Select(l => (double)l.Words / l.TargetWords).Order().ToList();
        var durations = part.Where(l => l.Raw is not null || l.Status == NotesProbe.TimedOut).Select(l => (double)l.DurationMs).Order().ToList();
        return new ProbeFigures(
            size,
            part.Count,
            part.Count(l => l.Status == nameof(WriterNotesStatus.Ok)),
            part.Count(l => l.Status == nameof(WriterNotesStatus.Absent)),
            part.Count(l => l.Status == nameof(WriterNotesStatus.Unreadable)),
            part.Count(l => l.Raw is null),
            part.Count(l => l.Damage.Count > 0),
            part.Where(l => l.Damage.Count > 0).Select(l => $"{l.Id}: {string.Join(",", l.Damage)}, last line \"{l.LastLine}\"").ToList(),
            EvalBasics.Percentile(shares, 0.5),
            EvalBasics.Percentile(durations, 0.9));
    }

    /// <summary>Why the result file cannot be judged; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<ProbeLine> lines, IReadOnlyDictionary<string, string> currentHashes)
    {
        var other = lines.Where(l => !currentHashes.TryGetValue(l.Id, out var hash) || hash != l.InputHash).Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
        return other.Count == 0 ? [] : [$"{other.Count} orders have results for another input or are unknown ({EvalBasics.Few(other)}); start a new result file"];
    }

    /// <summary>The outcome A, B or C, or not evaluable with the next stage to run.</summary>
    public static (ProbeOutcome Outcome, string Detail) Decide(IReadOnlyList<ProbeLine> lines, IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
            return (ProbeOutcome.NotEvaluable, string.Join("; ", problems));
        var shortFigures = Figures("short", lines);
        var longFigures = Figures("long", lines);
        var figures = $"{shortFigures} | {longFigures}";

        if (shortFigures.Damaged + longFigures.Damaged > 0)
            return (ProbeOutcome.C, $"damage found (counts even once); {figures}");
        if (shortFigures.Attempts < ShortAttempts || longFigures.Attempts < LongAttemptsClean)
        {
            var clean = shortFigures.Ok == shortFigures.Attempts && longFigures.Ok == longFigures.Attempts;
            var next = clean
                ? $"so far every attempt Ok: next stage b (--short {ShortAttempts} --long {LongAttemptsClean})"
                : $"an attempt was not Ok: next stage c (--short {ShortAttempts} --long {LongAttemptsFull})";
            return (ProbeOutcome.NotEvaluable, $"{next}; {figures}");
        }
        if (shortFigures.OkRate < ShortOkForNotC)
            return (ProbeOutcome.C, $"short texts below {ShortOkForNotC:P0} Ok; {figures}");

        var errorFree = shortFigures.Ok == shortFigures.Attempts && longFigures.Ok == longFigures.Attempts;
        if (errorFree)
            return (ProbeOutcome.A, $"every attempt Ok and undamaged; {figures}");
        if (longFigures.Attempts < LongAttemptsFull)
            return (ProbeOutcome.NotEvaluable, $"an attempt was not Ok: fill up to --short {ShortAttempts} --long {LongAttemptsFull} (stage c); {figures}");
        if (shortFigures.OkRate >= ShortOkForA && longFigures.OkRate >= LongOkForA)
            return (ProbeOutcome.A, figures);
        return (ProbeOutcome.B, $"no damage, but not every text has its block; {figures}");
    }
}
