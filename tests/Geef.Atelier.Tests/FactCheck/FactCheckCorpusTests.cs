using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Tools.FactCheckEval;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>
/// Replays the stored raw answers of <c>FactCheckEval run</c> (<c>FactCheck/Corpus/results-*.jsonl</c>) through the current
/// parser and filter without a model call, so every filter change is measurable at once. Without a result file every part of
/// gate V2 must be "not evaluable"; with one, the file must belong to the current corpus and prompt (no other input, no unknown
/// text), and the verdicts are printed for the person who decides on the fact-check mode.
/// </summary>
public sealed class FactCheckCorpusTests(ITestOutputHelper output)
{
    public const string Corpus = "factcheck-corpus.jsonl";
    public const string ProbeOrders = "notes-probe.jsonl";

    private static readonly TimeSpan Limit = new DirectOptions().FactCheckTimeout;

    public static string CorpusDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Geef.Atelier.Tests", "FactCheck", "Corpus");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "FactCheck", "Corpus");
    }

    [Fact]
    public void Stored_results_belong_to_the_current_corpus_and_get_a_verdict_for_every_part()
    {
        var directory = CorpusDirectory();
        var bases = FactCheckCorpus.Load(Path.Combine(directory, Corpus));
        var resultFiles = Directory.Exists(directory) ? Directory.GetFiles(directory, "results-*.jsonl").Order().ToList() : [];
        if (resultFiles.Count == 0)
        {
            var verdicts = FactCheckEvaluation.Verdicts(bases, [], FactCheckKit.Real, Limit);
            output.WriteLine($"No results yet in {directory}; every part of gate V2 is not evaluable.");
            Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
            return;
        }

        foreach (var file in resultFiles)
        {
            var lines = ResultFile.Load<FactCheckLine>(file);
            var problems = FactCheckEvaluation.DataProblems(bases, lines, FactCheckKit.Real);
            Assert.True(problems.Count == 0, $"{Path.GetFileName(file)} cannot be judged: {string.Join("; ", problems)}");
            output.WriteLine($"{Path.GetFileName(file)} {FactCheckEvaluation.DevelopmentFigures(bases, lines, FactCheckKit.Real, Limit)}");
            foreach (var verdict in FactCheckEvaluation.Verdicts(bases, lines, FactCheckKit.Real, Limit))
                output.WriteLine($"{Path.GetFileName(file)} {verdict}");
        }
    }

    /// <summary>
    /// The result file of the V2 calibration (round 1, 2026-11-25). Gate V2 was <b>not passed</b> on it, and this test keeps it that
    /// way on record (decision log D-073): every quality figure on the held-out set passed (17 of 17 planted or labelled mistakes hit,
    /// 0.20 blocking findings per clean text, 80 % of clean texts without one, no source conflict reported for a text without a faulty
    /// file, parents' evening found), but 2 of 12 calls hung until the time limit, so validity is 83.3 % (10 of 12; under the 95 % of
    /// the gate and under the 90 % below which the gate is not evaluable) and p90 is above 120 s. The fact check ships in
    /// <c>Report</c> as a documented deviation. The expectation is deliberately narrow: only validity and latency may miss, by exactly
    /// the measured amount, and every quality figure of both sets must stay at least as good as measured – hits of questions a and b,
    /// blocking findings on clean texts and beyond the planted mistakes on faulty texts, question c (contradictions in the files),
    /// conflicts on texts without a faulty file – and the number of kept findings must stay exactly as measured, so a stricter or
    /// looser filter that loses a hit, drops source conflicts or adds a false alarm turns this test red. The figures are those of the
    /// filter after the first review round of stage 3 (unsafe reasons replaced instead of dropping the finding, whole words for
    /// "covered word for word"); on this file they equal the figures measured before.
    /// </summary>
    public const string V2Calibration = "results-20261125-r1.jsonl";

    [Fact]
    public void The_v2_calibration_stays_not_passed_misses_only_validity_and_latency_and_keeps_its_quality()
    {
        var directory = CorpusDirectory();
        var path = Path.Combine(directory, V2Calibration);
        Assert.True(File.Exists(path), $"{V2Calibration} is missing; it is the record of gate V2 (D-073).");
        var bases = FactCheckCorpus.Load(Path.Combine(directory, Corpus));
        var lines = ResultFile.Load<FactCheckLine>(path);
        Assert.Empty(FactCheckEvaluation.DataProblems(bases, lines, FactCheckKit.Real));
        Assert.Empty(FactCheckEvaluation.MissingResults(bases, lines));

        var verdicts = FactCheckEvaluation.Verdicts(bases, lines, FactCheckKit.Real, Limit);
        Assert.NotEqual(GateVerdict.Passed, verdicts[^1].Verdict);
        Assert.Equal(FactCheckEvaluation.Gate, verdicts[^1].Gate);

        var holdout = FactCheckEvaluation.Replay(bases, lines, FactCheckKit.Real).Where(r => r.Item.Split == EvalBasics.Holdout).ToList();
        var calls = FactCheckEvaluation.Calls(holdout, Limit);
        Assert.Equal((12, 10, 2, 10), (calls.Calls, calls.Texts, calls.Retries, calls.Valid));
        Assert.True(calls.Validity < FactCheckEvaluation.EvaluableValidity);
        Assert.True(calls.P90Ms > FactCheckEvaluation.MaxP90Ms);
        Assert.Equal(0, calls.OverProductionLimit);
        Assert.Equal(0, calls.Unreplayable);
        Assert.All(holdout.Where(r => !FactCheckEvaluation.IsValid(r, Limit)), r => Assert.Equal(CallStatus.TimedOut, r.Line.Status));

        var quality = FactCheckEvaluation.Quality(FactCheckEvaluation.LastValid(holdout, Limit), FactCheckKit.Real);
        var dev = FactCheckEvaluation.Replay(bases, lines, FactCheckKit.Real).Where(r => r.Item.Split == EvalBasics.Dev).ToList();
        var development = FactCheckEvaluation.Quality(FactCheckEvaluation.LastValid(dev, Limit), FactCheckKit.Real);
        foreach (var (name, q) in new[] { ("held-out", quality), ("development", development) })
            output.WriteLine($"{name}: answered {q.Answered} ({q.CleanAnswered} clean, {q.FaultyAnswered} faulty), hits {q.Hits}/{q.Expected}, blocking on clean {q.BlockingOnClean}, " +
                $"clean without blocking {q.CleanWithoutBlocking}, blocking on faulty beyond the planted {q.BlockingOnFaultyBeyondErrors}, question c {q.SourceErrorHits}/{q.SourceErrors}, " +
                $"conflicts without faulty file {q.ConflictsWithoutFaultySource}, kept {q.Kept}, trace {string.Join(",", q.Trace.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"))}");
        Assert.Equal((5, 5, 0), (quality.CleanAnswered, quality.FaultyAnswered, quality.Unlocatable));
        Assert.Equal(17, quality.Expected);
        Assert.Equal(17, quality.Hits);
        Assert.True(quality.BlockingOnClean <= 1, $"{quality.BlockingOnClean} blocking findings on 5 clean texts; measured 1");
        Assert.True(quality.CleanWithoutBlocking >= 4, $"{quality.CleanWithoutBlocking} of 5 clean texts without a blocking finding; measured 4");
        Assert.Equal(0, quality.ConflictsWithoutFaultySource);
        Assert.True(quality.HitRate >= FactCheckEvaluation.MinHitRate);
        Assert.True(quality.BlockingPerClean <= FactCheckEvaluation.MaxBlockingPerClean);
        Assert.True(quality.CleanWithoutBlockingRate >= FactCheckEvaluation.MinCleanWithoutBlocking);
        Assert.True(quality.ConflictsPerText <= FactCheckEvaluation.MaxConflictsPerText);

        Assert.Equal((2, 2), (quality.SourceErrors, quality.SourceErrorHits));
        Assert.True(quality.BlockingOnFaultyBeyondErrors <= 1, $"{quality.BlockingOnFaultyBeyondErrors} blocking findings beyond the planted mistakes; measured 1");
        Assert.Equal(21, quality.Kept);

        Assert.Equal((8, 10, 0), (development.CleanAnswered, development.FaultyAnswered, development.Unlocatable));
        Assert.Equal((33, 33), (development.Expected, development.Hits));
        Assert.True(development.BlockingOnClean <= 3, $"{development.BlockingOnClean} blocking findings on 8 clean development texts; measured 3");
        Assert.True(development.CleanWithoutBlocking >= 6, $"{development.CleanWithoutBlocking} of 8 clean development texts without a blocking finding; measured 6");
        Assert.True(development.BlockingOnFaultyBeyondErrors <= 1, $"{development.BlockingOnFaultyBeyondErrors} blocking findings beyond the planted mistakes in the development set; measured 1");
        Assert.Equal((2, 2), (development.SourceErrors, development.SourceErrorHits));
        Assert.Equal(0, development.ConflictsWithoutFaultySource);
        Assert.Equal(39, development.Kept);

        var parentsEvening = verdicts.Single(v => v.Gate == $"{FactCheckEvaluation.Gate} parents' evening");
        Assert.Equal(GateVerdict.Passed, parentsEvening.Verdict);
        Assert.Contains("question c yes", parentsEvening.Detail, StringComparison.Ordinal);
        Assert.All(verdicts.Where(v => v.Gate != FactCheckEvaluation.Gate && v != parentsEvening),
            v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
    }
}
