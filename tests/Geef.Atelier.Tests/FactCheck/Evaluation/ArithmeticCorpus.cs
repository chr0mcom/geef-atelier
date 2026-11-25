using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>
/// The calculation in a snippet as the generator wrote it: the line with the total, the items as written, the operators between
/// them (all <c>+</c> when empty), the stated total and the right total.
/// </summary>
public sealed record ArithmeticTrace(
    [property: JsonPropertyName("total_line")] string TotalLine,
    [property: JsonPropertyName("items")] IReadOnlyList<string> Items,
    [property: JsonPropertyName("ops")] IReadOnlyList<string>? Ops,
    [property: JsonPropertyName("stated")] string Stated,
    [property: JsonPropertyName("correct")] string Correct);

/// <summary>
/// One snippet of the arithmetic corpus (V3, <c>T/Domain/Checks/Corpus/arithmetic-corpus.jsonl</c>, written by
/// <c>factcheck_corpus.py arithmetic</c>): a planted wrong total with its trace, a right calculation with its trace, or numbers
/// without any calculation (no trace).
/// </summary>
public sealed record ArithmeticEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("form")] string Form,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("planted")] bool Planted,
    [property: JsonPropertyName("trace")] ArithmeticTrace? Trace)
{
    /// <summary>Split by the hash of the snippet text.</summary>
    public string Split => EvalBasics.SplitOf(Text);
}

/// <summary>
/// Judges gate V3 of contract stage 3, 3.3 on the held-out set: hit rate at least 0.80 over planted wrong totals, at most one
/// false alarm per nine clean snippets. No gate passes without data: wrong corpus size or mix, a trace the code cannot
/// recompute, fewer than <see cref="MinPerKind"/> planted or clean held-out snippets, or a check that throws make it not evaluable.
/// </summary>
public static class ArithmeticCorpusEvaluation
{
    /// <summary>Snippets of the corpus.</summary>
    public const int ExpectedEntries = 60;

    /// <summary>Snippets with a planted wrong total.</summary>
    public const int ExpectedPlanted = 30;

    /// <summary>Lowest hit rate.</summary>
    public const double MinHitRate = 0.80;

    /// <summary>Highest share of clean snippets with a report (one of nine).</summary>
    public const double MaxFalseAlarmRate = 1.0 / 9;

    /// <summary>Fewest planted and clean snippets in the held-out set.</summary>
    public const int MinPerKind = 5;

    /// <summary>Gate name.</summary>
    public const string Gate = "V3";

    /// <summary>Loads the snippets of a JSONL file; a missing file has none.</summary>
    public static IReadOnlyList<ArithmeticEntry> Load(string path) => ResultFile.Load<ArithmeticEntry>(path);

    /// <summary>The total the items give with their operators (multiplication before addition and subtraction), or null when an item cannot be read.</summary>
    public static decimal? Recalculate(ArithmeticTrace trace)
    {
        var values = trace.Items.Select(Amounts.Parse).ToList();
        if (values.Count == 0 || values.Any(v => v is null))
            return null;
        var ops = trace.Ops is { Count: > 0 } given ? given : Enumerable.Repeat("+", values.Count - 1).ToList();
        if (ops.Count != values.Count - 1)
            return null;
        var terms = new List<decimal> { values[0]!.Value };
        var signs = new List<int> { 1 };
        for (var i = 0; i < ops.Count; i++)
        {
            var value = values[i + 1]!.Value;
            switch (ops[i])
            {
                case "+":
                    terms.Add(value);
                    signs.Add(1);
                    break;
                case "-" or "−":
                    terms.Add(value);
                    signs.Add(-1);
                    break;
                case "*" or "×" or "x" or "·":
                    terms[^1] *= value;
                    break;
                default:
                    return null;
            }
        }
        return terms.Select((t, i) => t * signs[i]).Sum();
    }

    /// <summary>Why a snippet's trace does not hold; null when it does.</summary>
    public static string? TraceProblem(ArithmeticEntry entry)
    {
        if (entry.Trace is not { } trace)
            return entry.Planted ? "planted without trace" : null;
        if (!entry.Text.Contains(trace.TotalLine, StringComparison.Ordinal))
            return "total line not in the text";
        if (!trace.TotalLine.Contains(trace.Stated, StringComparison.Ordinal))
            return "stated total not in the total line";
        if (trace.Items.Any(i => !entry.Text.Contains(i, StringComparison.Ordinal)))
            return "an item is not in the text";
        if (Recalculate(trace) is not { } computed || Amounts.Parse(trace.Stated) is not { } stated || Amounts.Parse(trace.Correct) is not { } correct)
            return "the numbers cannot be read";
        if (Math.Abs(computed - correct) > SourceArithmeticCheck.Tolerance)
            return $"the items give {computed}, not the right total {trace.Correct}";
        var adds = Math.Abs(computed - stated) <= SourceArithmeticCheck.Tolerance;
        return entry.Planted == adds ? (entry.Planted ? "planted, but the stated total is right" : "clean, but the stated total is wrong") : null;
    }

    /// <summary>Why the corpus cannot be judged; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<ArithmeticEntry> entries)
    {
        var problems = new List<string>();
        if (entries.Count != ExpectedEntries)
            problems.Add($"the corpus has {entries.Count} of {ExpectedEntries} snippets");
        var planted = entries.Count(e => e.Planted);
        if (planted != ExpectedPlanted)
            problems.Add($"{planted} of {ExpectedPlanted} snippets have a planted wrong total");
        var duplicate = entries.GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicate.Count > 0)
            problems.Add($"duplicate ids ({EvalBasics.Few(duplicate)})");
        var broken = entries.Select(e => (e.Id, Problem: TraceProblem(e))).Where(x => x.Problem is not null).ToList();
        if (broken.Count > 0)
            problems.Add($"{broken.Count} traces do not hold ({EvalBasics.Few(broken.Select(b => $"{b.Id}: {b.Problem}"))})");
        var holdout = entries.Where(e => e.Split == EvalBasics.Holdout).ToList();
        if (holdout.Count(e => e.Planted) < MinPerKind || holdout.Count(e => !e.Planted) < MinPerKind)
            problems.Add($"the held-out set has {holdout.Count(e => e.Planted)} planted and {holdout.Count(e => !e.Planted)} clean snippets (need {MinPerKind} each)");
        return problems;
    }

    /// <summary>Hits on planted snippets and false alarms on clean ones of a part.</summary>
    public static (int Planted, int Hits, int Clean, int FalseAlarms, IReadOnlyList<string> Missed, IReadOnlyList<string> Alarms) Measure(
        IReadOnlyList<ArithmeticEntry> entries, Func<IReadOnlyList<(string SourceId, string FileName, string Text)>, SourceArithmeticResult> check)
    {
        var missed = new List<string>();
        var alarms = new List<string>();
        int hits = 0, falseAlarms = 0;
        foreach (var entry in entries)
        {
            var issues = check([("S1", $"{entry.Id}.txt", entry.Text)]).Issues;
            if (entry.Planted)
            {
                if (issues.Any(i => Amounts.Same(i.Stated, entry.Trace!.Stated)))
                    hits++;
                else
                    missed.Add(entry.Id);
            }
            else if (issues.Count > 0)
            {
                falseAlarms++;
                alarms.Add(entry.Id);
            }
        }
        return (entries.Count(e => e.Planted), hits, entries.Count(e => !e.Planted), falseAlarms, missed, alarms);
    }

    /// <summary>The figures of the development set (for rule changes) and the verdict of gate V3 on the held-out set.</summary>
    public static IReadOnlyList<GateResult> Verdicts(IReadOnlyList<ArithmeticEntry> entries, Func<IReadOnlyList<(string SourceId, string FileName, string Text)>, SourceArithmeticResult> check)
    {
        var problems = Problems(entries);
        if (problems.Count > 0)
            return [GateResult.NotEvaluable(Gate, EvalBasics.Holdout, string.Join("; ", problems))];
        var results = new List<GateResult>();
        foreach (var split in new[] { EvalBasics.Dev, EvalBasics.Holdout })
        {
            var part = entries.Where(e => e.Split == split).ToList();
            try
            {
                var m = Measure(part, check);
                var hitRate = m.Planted == 0 ? 0 : (double)m.Hits / m.Planted;
                var alarmRate = m.Clean == 0 ? 0 : (double)m.FalseAlarms / m.Clean;
                var detail = $"{m.Hits} of {m.Planted} wrong totals found = {hitRate:P1} (need {MinHitRate:P0}), false alarms on {m.FalseAlarms} of {m.Clean} clean snippets = {alarmRate:P1} (at most 1 of 9)" +
                    $"{(m.Missed.Count > 0 ? $"; missed {EvalBasics.Few(m.Missed)}" : string.Empty)}{(m.Alarms.Count > 0 ? $"; alarms {EvalBasics.Few(m.Alarms)}" : string.Empty)}";
                results.Add(split == EvalBasics.Holdout
                    ? GateResult.Of(Gate, split, true, hitRate >= MinHitRate && alarmRate <= MaxFalseAlarmRate + 1e-9, detail)
                    : new GateResult($"{Gate} figures", split, GateVerdict.NotEvaluable, $"development set, never a verdict: {detail}"));
            }
            catch (NotImplementedException)
            {
                results.Add(GateResult.NotEvaluable(split == EvalBasics.Holdout ? Gate : $"{Gate} figures", split, "SourceArithmeticCheck is not implemented yet (package R1)"));
            }
        }
        return results;
    }
}
