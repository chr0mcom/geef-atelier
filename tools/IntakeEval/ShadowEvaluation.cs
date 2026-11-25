using System.Globalization;
using System.Text;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>One row of the CSV export of <c>OrderIntakeShadows</c> (see the README of the tool for the read-only query).</summary>
public sealed record ShadowRow(
    string OrderId,
    string Status,
    int? DurationMs,
    int? QuestionCount,
    string? ErrorCode,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<string> Questions);

/// <summary>Figures of the shadow mode: which rows count where, latency, questions and failures.</summary>
public sealed record ShadowFigures(
    int Rows,
    int Finished,
    int Done,
    int Failed,
    int TimedOut,
    int PrepareFailed,
    int SkippedCap,
    int Pending,
    int Other,
    int Timed,
    double MedianMs,
    double P90Ms,
    int WithQuestionCount,
    double MeanQuestions,
    double FailureRate,
    TimeSpan Span);

/// <summary>
/// Gate g and the shadow parts of gates a and f from a CSV export of <c>OrderIntakeShadows</c>. Rows that never ran
/// (<c>SkippedCap</c>, <c>Pending</c>) are left out and counted. Only answers (<c>Done</c>) and time-outs carry a latency; other
/// failures (also a failed preparation, <c>prepare_failed</c>) count as failures but never as latency, so that fast failures never
/// make the median look better. A missing question count (failures) never counts as zero questions.
/// </summary>
public static class ShadowEvaluation
{
    /// <summary>Gate g: fewest finished shadow orders, unless they span <see cref="MinSpan"/>.</summary>
    public const int MinFinished = 30;

    /// <summary>Gate g: shortest span of the shadow orders, unless there are <see cref="MinFinished"/>.</summary>
    public static readonly TimeSpan MinSpan = TimeSpan.FromDays(7);

    /// <summary>Error code of a failed preparation (files or profile could not be read).</summary>
    public const string PrepareFailedCode = "prepare_failed";

    /// <summary>Reads the export: a header line, then one row per order; quoted fields may hold commas, quotes and line breaks.</summary>
    public static IReadOnlyList<ShadowRow> Read(TextReader reader)
    {
        var records = Csv(reader).ToList();
        if (records.Count == 0)
            return [];
        var header = records[0].Select((name, index) => (name.Trim(), index)).ToDictionary(h => h.Item1, h => h.index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "OrderId", "Status", "DurationMs", "QuestionCount", "ErrorCode", "CreatedAt" })
        {
            if (!header.ContainsKey(required))
                throw new FormatException($"The export has no column {required}.");
        }

        string? Field(IReadOnlyList<string> row, string name) =>
            header.TryGetValue(name, out var i) && i < row.Count && row[i].Length > 0 ? row[i] : null;

        return records.Skip(1)
            .Where(r => r.Count > 1 || (r.Count == 1 && r[0].Length > 0))
            .Select(r => new ShadowRow(
                Field(r, "OrderId") ?? string.Empty,
                Field(r, "Status") ?? string.Empty,
                Field(r, "DurationMs") is { } d ? int.Parse(d, CultureInfo.InvariantCulture) : null,
                Field(r, "QuestionCount") is { } q ? int.Parse(q, CultureInfo.InvariantCulture) : null,
                Field(r, "ErrorCode"),
                Field(r, "CreatedAt") is { } c ? DateTimeOffset.Parse(c, CultureInfo.InvariantCulture) : null,
                Field(r, "Questions") is { } texts ? texts.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : []))
            .ToList();
    }

    /// <summary>The figures of the shadow rows.</summary>
    public static ShadowFigures Measure(IReadOnlyList<ShadowRow> rows)
    {
        var finished = rows.Where(r => r.Status is IntakeShadowStatuses.Done or IntakeShadowStatuses.Failed or IntakeShadowStatuses.TimedOut).ToList();
        var timed = finished
            .Where(r => r.Status is IntakeShadowStatuses.Done or IntakeShadowStatuses.TimedOut)
            .Where(r => r.DurationMs is > 0 && !string.Equals(r.ErrorCode, PrepareFailedCode, StringComparison.Ordinal))
            .Select(r => (double)r.DurationMs!.Value)
            .Order()
            .ToList();
        var counted = finished.Where(r => r.Status == IntakeShadowStatuses.Done && r.QuestionCount is not null).Select(r => r.QuestionCount!.Value).ToList();
        var failures = finished.Count(r => r.Status != IntakeShadowStatuses.Done);
        var created = finished.Where(r => r.CreatedAt is not null).Select(r => r.CreatedAt!.Value).ToList();
        return new ShadowFigures(
            rows.Count,
            finished.Count,
            finished.Count(r => r.Status == IntakeShadowStatuses.Done),
            finished.Count(r => r.Status == IntakeShadowStatuses.Failed),
            finished.Count(r => r.Status == IntakeShadowStatuses.TimedOut),
            finished.Count(r => string.Equals(r.ErrorCode, PrepareFailedCode, StringComparison.Ordinal)),
            rows.Count(r => r.Status == IntakeShadowStatuses.SkippedCap),
            rows.Count(r => r.Status == IntakeShadowStatuses.Pending),
            rows.Count - finished.Count - rows.Count(r => r.Status is IntakeShadowStatuses.SkippedCap or IntakeShadowStatuses.Pending),
            timed.Count,
            CorpusEvaluation.Percentile(timed, 0.5),
            CorpusEvaluation.Percentile(timed, 0.9),
            counted.Count,
            counted.Count == 0 ? 0 : counted.Average(),
            finished.Count == 0 ? 0 : (double)failures / finished.Count,
            created.Count == 0 ? TimeSpan.Zero : created.Max() - created.Min());
    }

    /// <summary>Gates a and f for the shadow, the measurable part of gate g and the visual check of g, which is always left to a person.</summary>
    public static IReadOnlyList<GateResult> Verdicts(ShadowFigures f)
    {
        const string scope = "shadow";
        var excluded = $"left out: {f.SkippedCap} skipped (cap), {f.Pending} pending, {f.Other} other";
        var enough = f.Finished >= MinFinished || f.Span >= MinSpan;

        GateResult Result(string gate, bool evaluable, bool passed, string detail) =>
            new(gate, scope, !evaluable ? GateVerdict.NotEvaluable : passed ? GateVerdict.Passed : GateVerdict.Failed, detail);

        return
        [
            Result("a", f.Timed > 0, f.MedianMs <= CorpusEvaluation.MaxMedianMs && f.P90Ms <= CorpusEvaluation.MaxP90Ms,
                $"median {f.MedianMs / 1000:0.0} s, p90 {f.P90Ms / 1000:0.0} s over {f.Timed} answers and time-outs (other failures without latency); {excluded}"),
            Result("f", f.Finished > 0, f.FailureRate <= CorpusEvaluation.MaxFailureRate,
                $"{f.FailureRate:P1} failures ({f.Failed} failed, {f.TimedOut} timed out) over {f.Finished} finished; {excluded}"),
            Result("g", enough && f.WithQuestionCount > 0 && f.Timed > 0,
                f.MeanQuestions <= CorpusEvaluation.MaxMeanQuestions && f.MedianMs <= CorpusEvaluation.MaxMedianMs,
                $"mean {f.MeanQuestions:0.00} questions over {f.WithQuestionCount} answered, median {f.MedianMs / 1000:0.0} s; {f.Finished} finished over {f.Span.TotalDays:0.0} days " +
                $"(needs {MinFinished} or {MinSpan.TotalDays:0} days)"),
            new("g", "visual check", GateVerdict.NotEvaluable, "ten questions must be checked by an advisor; the tool prints a sample"),
        ];
    }

    /// <summary>Up to <paramref name="count"/> questions spread over the export, for the visual check of gate g.</summary>
    public static IReadOnlyList<string> Sample(IReadOnlyList<ShadowRow> rows, int count)
    {
        var all = rows.SelectMany(r => r.Questions).ToList();
        if (all.Count <= count)
            return all;
        return Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToList();
    }

    private static IEnumerable<IReadOnlyList<string>> Csv(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;
        int read;
        while ((read = reader.Read()) >= 0)
        {
            var c = (char)read;
            any = true;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    any = false;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (any)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
