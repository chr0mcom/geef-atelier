using Geef.Atelier.Core.Domain.FactCheck;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>A stored answer replayed through the current parser and filter; <see cref="Result"/> is null when it cannot be replayed.</summary>
public sealed record FactCheckReplay(FactCheckItem Item, FactCheckLine Line, FactFilterResult? Result);

/// <summary>Figures over every call line of a part (every attempt, also repeated ones).</summary>
public sealed record FactCheckCallFigures(int Calls, int Texts, int Retries, int Valid, double Validity, int Timed, double MedianMs, double P90Ms, int OverProductionLimit, int Unreplayable);

/// <summary>Figures over the last valid answer of each text of a part.</summary>
public sealed record FactCheckQualityFigures(
    int Answered,
    int CleanAnswered,
    int FaultyAnswered,
    int Expected,
    int Hits,
    int BlockingOnClean,
    int CleanWithoutBlocking,
    int SourceErrors,
    int SourceErrorHits,
    int TextsWithoutFaultySource,
    int ConflictsWithoutFaultySource,
    int Unlocatable,
    IReadOnlyDictionary<FactFilterDecision, int> Trace,
    int BlockingOnFaultyBeyondErrors = 0,
    int Kept = 0)
{
    /// <summary>Share of expected mistakes (questions a, b, superseded) hit by a blocking finding.</summary>
    public double HitRate => Expected == 0 ? 0 : (double)Hits / Expected;

    /// <summary>Blocking findings per clean text.</summary>
    public double BlockingPerClean => CleanAnswered == 0 ? 0 : (double)BlockingOnClean / CleanAnswered;

    /// <summary>Share of clean texts without any blocking finding.</summary>
    public double CleanWithoutBlockingRate => CleanAnswered == 0 ? 0 : (double)CleanWithoutBlocking / CleanAnswered;

    /// <summary>Reported source conflicts per text whose material has no mistake of its own.</summary>
    public double ConflictsPerText => TextsWithoutFaultySource == 0 ? 0 : (double)ConflictsWithoutFaultySource / TextsWithoutFaultySource;

    /// <summary>Share of mistakes in the material (question c) reported as a source conflict.</summary>
    public double SourceHitRate => SourceErrors == 0 ? 0 : (double)SourceErrorHits / SourceErrors;
}

/// <summary>
/// Replays the stored answers of <c>FactCheckEval run</c> through the current parser and filter and judges gate V2 of contract
/// stage 3, 3.2 on the held-out set. No gate passes without data: an incomplete corpus, results for another input or without a
/// fingerprint, results for unknown texts, a held-out text or the parents' evening without any result, fewer than 3 answered
/// clean or faulty held-out texts or a validity below 90 % make the gate "not evaluable". Validity counts every attempt; the
/// latency counts answers and time-outs only; an answer over the production limit or one that cannot be replayed is invalid.
/// </summary>
public static class FactCheckEvaluation
{
    /// <summary>Lowest hit rate (questions a, b and superseded).</summary>
    public const double MinHitRate = 0.60;

    /// <summary>Most blocking findings per clean text.</summary>
    public const double MaxBlockingPerClean = 0.25;

    /// <summary>Lowest share of clean texts without a blocking finding.</summary>
    public const double MinCleanWithoutBlocking = 0.75;

    /// <summary>Most reported source conflicts per text whose material has no mistake.</summary>
    public const double MaxConflictsPerText = 0.15;

    /// <summary>Lowest share of valid answers for the gate.</summary>
    public const double MinValidity = 0.95;

    /// <summary>Below this share of valid answers the gate is not evaluable.</summary>
    public const double EvaluableValidity = 0.90;

    /// <summary>Highest 90th percentile latency in milliseconds.</summary>
    public const double MaxP90Ms = 120_000;

    /// <summary>Fewest answered clean and faulty held-out texts.</summary>
    public const int MinTexts = 3;

    /// <summary>Gate name.</summary>
    public const string Gate = "V2";

    /// <summary>Ids of the texts whose last result for the current input is <c>Ok</c> or <c>Invalid</c>; <c>run</c> skips them.</summary>
    public static IReadOnlySet<string> Answered(IReadOnlyList<FactCheckLine> lines, IReadOnlyDictionary<string, string> currentHashes) =>
        lines.Where(l => currentHashes.TryGetValue(l.Id, out var hash) && l.InputHash == hash)
            .GroupBy(l => l.Id, StringComparer.Ordinal)
            .Where(g => g.Last().Status is CallStatus.Ok or CallStatus.Invalid)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Why the result file cannot be judged at all; empty when it can.</summary>
    public static IReadOnlyList<string> DataProblems(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckLine> lines, FactCheckKit kit)
    {
        var problems = FactCheckCorpus.Problems(bases).ToList();
        var items = FactCheckCorpus.Items(bases).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var unknown = lines.Select(l => l.Id).Where(id => !items.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            problems.Add($"{unknown.Count} results belong to no text of the corpus ({EvalBasics.Few(unknown)})");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var other = lines
            .Where(l => items.TryGetValue(l.Id, out var item) && (l.Split != item.Split || l.InputHash != Hash(item, kit, hashes)))
            .Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
        if (other.Count > 0)
            problems.Add($"{other.Count} texts have results made for another input (text, material, prompt or schema changed, or no input hash); start a new result file ({EvalBasics.Few(other)})");
        return problems;
    }

    /// <summary>Held-out texts and the planted parents' evening without any result line.</summary>
    public static IReadOnlyList<string> MissingResults(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckLine> lines)
    {
        var answered = lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var required = FactCheckCorpus.Items(bases)
            .Where(i => i.Split == EvalBasics.Holdout || (i.Basis.Case == FactCheckCorpus.ParentsEvening && i.Variant == "planted"))
            .Select(i => i.Id)
            .Where(id => !answered.Contains(id))
            .ToList();
        return required.Count == 0 ? [] : [$"{required.Count} required texts have no result ({EvalBasics.Few(required)})"];
    }

    /// <summary>Replays every line of the file.</summary>
    public static IReadOnlyList<FactCheckReplay> Replay(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckLine> lines, FactCheckKit kit)
    {
        var items = FactCheckCorpus.Items(bases).ToDictionary(i => i.Id, StringComparer.Ordinal);
        return lines.Where(l => items.ContainsKey(l.Id)).Select(l =>
        {
            var item = items[l.Id];
            if (l.Status != CallStatus.Ok || l.Raw is null || kit.Parse(l.Raw) is not { } proposed)
                return new FactCheckReplay(item, l, null);
            return new FactCheckReplay(item, l, kit.Filter(proposed, item.Text, item.Material));
        }).ToList();
    }

    /// <summary>True when the line is a usable answer: <c>Ok</c>, within <paramref name="productionLimit"/> and replayable.</summary>
    public static bool IsValid(FactCheckReplay replay, TimeSpan productionLimit) =>
        replay.Line.Status == CallStatus.Ok && replay.Line.DurationMs <= productionLimit.TotalMilliseconds && replay.Result is not null;

    /// <summary>Validity and latency over every line of a part.</summary>
    public static FactCheckCallFigures Calls(IReadOnlyList<FactCheckReplay> lines, TimeSpan productionLimit)
    {
        var durations = lines.Where(r => CallStatus.HasLatency(r.Line.Status)).Select(r => (double)r.Line.DurationMs).Order().ToList();
        var valid = lines.Count(r => IsValid(r, productionLimit));
        var texts = lines.Select(r => r.Line.Id).Distinct(StringComparer.Ordinal).Count();
        return new FactCheckCallFigures(
            lines.Count, texts, lines.Count - texts, valid, lines.Count == 0 ? 0 : (double)valid / lines.Count,
            durations.Count, EvalBasics.Percentile(durations, 0.5), EvalBasics.Percentile(durations, 0.9),
            lines.Count(r => r.Line.Status == CallStatus.Ok && r.Line.DurationMs > productionLimit.TotalMilliseconds),
            lines.Count(r => r.Line.Status == CallStatus.Ok && r.Result is null));
    }

    /// <summary>The last valid answer of each text.</summary>
    public static IReadOnlyList<FactCheckReplay> LastValid(IReadOnlyList<FactCheckReplay> lines, TimeSpan productionLimit) =>
        lines.Where(r => IsValid(r, productionLimit)).GroupBy(r => r.Line.Id, StringComparer.Ordinal).Select(g => g.Last()).ToList();

    /// <summary>Hit rate, false alarms, source conflicts and filter trace over answered texts only; a text without answer never counts as "nothing found".</summary>
    public static FactCheckQualityFigures Quality(IReadOnlyList<FactCheckReplay> answered, FactCheckKit kit)
    {
        int expected = 0, hits = 0, blockingOnClean = 0, cleanQuiet = 0, sourceErrors = 0, sourceHits = 0, plainTexts = 0, plainConflicts = 0, unlocatable = 0;
        int beyond = 0, kept = 0;
        var trace = new Dictionary<FactFilterDecision, int>();
        foreach (var replay in answered)
        {
            var result = replay.Result!;
            var item = replay.Item;
            foreach (var step in result.Trace)
                trace[step.Decision] = trace.GetValueOrDefault(step.Decision) + 1;
            var blocking = result.Findings.Where(f => f.Blocking && f.TextQuote is not null).ToList();
            var conflicts = result.Findings.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList();
            kept += result.Findings.Count;
            if (item.IsFaulty)
            {
                var places = item.Errors.Where(e => e.Question != FactCheckCorpus.QuestionC)
                    .Select(e => kit.Locate(item.Text, e.Wording)).OfType<(int, int)>().ToList();
                beyond += blocking.Count(f => kit.Locate(item.Text, f.TextQuote!) is not { } found || !places.Any(p => Overlap(p, found)));
            }

            foreach (var error in item.Errors.Where(e => e.Question != FactCheckCorpus.QuestionC))
            {
                expected++;
                if (kit.Locate(item.Text, error.Wording) is not { } place)
                {
                    unlocatable++;
                    continue;
                }
                if (blocking.Any(f => kit.Locate(item.Text, f.TextQuote!) is { } found && Overlap(place, found)))
                    hits++;
            }

            if (item.IsClean)
            {
                blockingOnClean += blocking.Count;
                if (blocking.Count == 0)
                    cleanQuiet++;
            }

            if (item.HasFaultySource)
            {
                foreach (var error in item.Basis.SourceErrors ?? [])
                {
                    sourceErrors++;
                    if (conflicts.Any(f => Reported(item, error, f, kit)))
                        sourceHits++;
                }
            }
            else
            {
                plainTexts++;
                plainConflicts += conflicts.Count;
            }
        }

        return new FactCheckQualityFigures(
            answered.Count, answered.Count(r => r.Item.IsClean), answered.Count(r => r.Item.IsFaulty), expected, hits,
            blockingOnClean, cleanQuiet, sourceErrors, sourceHits, plainTexts, plainConflicts, unlocatable, trace, beyond, kept);
    }

    /// <summary>
    /// The verdict of gate V2 and its parts on the held-out set, plus the mandatory parents' evening from the development set.
    /// With any data problem or missing result every part is <see cref="GateVerdict.NotEvaluable"/>. The last line is the whole
    /// gate: failed as soon as one part failed on its data, otherwise not evaluable while one part is, otherwise passed.
    /// </summary>
    public static IReadOnlyList<GateResult> Verdicts(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckLine> lines, FactCheckKit kit, TimeSpan productionLimit)
    {
        var parts = new[] { "hit rate", "false alarms", "source conflicts", "validity", "latency", "parents' evening" };
        var problems = DataProblems(bases, lines, kit).Concat(MissingResults(bases, lines)).ToList();
        if (problems.Count > 0)
        {
            var reason = string.Join("; ", problems);
            return [.. parts.Select(p => GateResult.NotEvaluable($"{Gate} {p}", EvalBasics.Holdout, reason)), GateResult.NotEvaluable(Gate, EvalBasics.Holdout, reason)];
        }

        var replays = Replay(bases, lines, kit);
        var holdout = replays.Where(r => r.Item.Split == EvalBasics.Holdout).ToList();
        var calls = Calls(holdout, productionLimit);
        var q = Quality(LastValid(holdout, productionLimit), kit);
        var enough = q.CleanAnswered >= MinTexts && q.FaultyAnswered >= MinTexts && calls.Validity >= EvaluableValidity && q.Unlocatable == 0;
        var why = $"{q.CleanAnswered} clean and {q.FaultyAnswered} faulty texts answered (need {MinTexts} each), validity {calls.Validity:P1} (need {EvaluableValidity:P0}), {q.Unlocatable} expected mistakes not found in their own text";
        string Detail(string figures) => enough ? figures : $"{why}; {figures}";
        var callDetail = $"{calls.Calls} calls for {calls.Texts} texts ({calls.Retries} repeated)";

        var results = new List<GateResult>
        {
            GateResult.Of($"{Gate} hit rate", EvalBasics.Holdout, enough && q.Expected > 0, q.HitRate >= MinHitRate,
                Detail($"{q.Hits} of {q.Expected} planted or labelled mistakes (a, b, superseded) hit by a blocking finding = {q.HitRate:P1} (need {MinHitRate:P0})")),
            GateResult.Of($"{Gate} false alarms", EvalBasics.Holdout, enough, q.BlockingPerClean <= MaxBlockingPerClean && q.CleanWithoutBlockingRate >= MinCleanWithoutBlocking,
                Detail($"{q.BlockingPerClean:0.00} blocking findings per clean text (at most {MaxBlockingPerClean:0.00}), {q.CleanWithoutBlockingRate:P1} of {q.CleanAnswered} clean texts without one (need {MinCleanWithoutBlocking:P0})")),
            GateResult.Of($"{Gate} source conflicts", EvalBasics.Holdout, enough && q.TextsWithoutFaultySource > 0, q.ConflictsPerText <= MaxConflictsPerText,
                Detail($"{q.ConflictsPerText:0.00} reported source conflicts per text without a faulty file over {q.TextsWithoutFaultySource} texts (at most {MaxConflictsPerText:0.00}); reported only: question c hit {q.SourceErrorHits} of {q.SourceErrors}")),
            GateResult.Of($"{Gate} validity", EvalBasics.Holdout, enough, calls.Validity >= MinValidity,
                Detail($"{calls.Validity:P1} valid ({calls.OverProductionLimit} over the production limit, {calls.Unreplayable} not replayable) over {callDetail}")),
            GateResult.Of($"{Gate} latency", EvalBasics.Holdout, enough && calls.Timed > 0, calls.P90Ms <= MaxP90Ms,
                Detail($"median {calls.MedianMs / 1000:0.0} s, p90 {calls.P90Ms / 1000:0.0} s over {calls.Timed} answers and time-outs (p90 at most {MaxP90Ms / 1000:0} s)")),
            ParentsEvening(bases, replays, kit, productionLimit),
        };

        var overall = results.Any(r => r.Verdict == GateVerdict.Failed) ? GateVerdict.Failed
            : results.Any(r => r.Verdict == GateVerdict.NotEvaluable) ? GateVerdict.NotEvaluable : GateVerdict.Passed;
        results.Add(new GateResult(Gate, EvalBasics.Holdout, overall, "all parts above; passed only when every part passed, failed as soon as one part failed"));
        return results;
    }

    /// <summary>The figures of the development set, for tuning prompt and filter (never a verdict).</summary>
    public static string DevelopmentFigures(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckLine> lines, FactCheckKit kit, TimeSpan productionLimit)
    {
        var dev = Replay(bases, lines, kit).Where(r => r.Item.Split == EvalBasics.Dev).ToList();
        var calls = Calls(dev, productionLimit);
        var q = Quality(LastValid(dev, productionLimit), kit);
        var trace = string.Join(", ", q.Trace.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
        return $"dev: {calls.Calls} calls for {calls.Texts} texts, validity {calls.Validity:P1}, median {calls.MedianMs / 1000:0.0} s, p90 {calls.P90Ms / 1000:0.0} s; " +
            $"hit rate {q.Hits}/{q.Expected}, blocking per clean text {q.BlockingPerClean:0.00} over {q.CleanAnswered}, clean without blocking {q.CleanWithoutBlockingRate:P1}, " +
            $"source conflicts per plain text {q.ConflictsPerText:0.00}, question c {q.SourceErrorHits}/{q.SourceErrors}; filter trace: {(trace.Length == 0 ? "none" : trace)}";
    }

    private static GateResult ParentsEvening(IReadOnlyList<CorpusBasis> bases, IReadOnlyList<FactCheckReplay> replays, FactCheckKit kit, TimeSpan productionLimit)
    {
        const string name = $"{Gate} parents' evening";
        var basis = bases.Single(b => b.Case == FactCheckCorpus.ParentsEvening);
        var answer = LastValid(replays.Where(r => r.Item.Basis.Id == basis.Id && r.Item.Variant == "planted").ToList(), productionLimit).SingleOrDefault();
        if (answer is null)
            return GateResult.NotEvaluable(name, EvalBasics.Dev, "the planted parents' evening has no valid answer");

        var item = answer.Item;
        var blocking = answer.Result!.Findings.Where(f => f.Blocking && f.TextQuote is not null).ToList();
        var contact = item.Errors.Where(e => e.Kind == FactCheckCorpus.InventedContact).ToList();
        var contactHit = contact.Count > 0 && contact.All(e =>
            kit.Locate(item.Text, e.Wording) is { } place && blocking.Any(f => kit.Locate(item.Text, f.TextQuote!) is { } found && Overlap(place, found)));

        var wrongTotal = basis.SourceErrors!.First(e => e.Kind == "wrong_total" && e.Stated is not null);
        var byQuestionC = answer.Result.Findings.Any(f => f.Kind == FactFindingKind.SourceConflict && Reported(item, wrongTotal, f, kit));
        bool? byArithmetic;
        try
        {
            var files = (basis.Files ?? []).Select(f => (f.Id, f.Name, f.Text)).ToList();
            byArithmetic = kit.Arithmetic(files).Issues.Any(i => Amounts.Same(i.Stated, wrongTotal.Stated));
        }
        catch (NotImplementedException)
        {
            byArithmetic = null;
        }

        var totalHit = byQuestionC || byArithmetic == true;
        var detail = $"wrong total {wrongTotal.Stated}: question c {(byQuestionC ? "yes" : "no")}, arithmetic check {byArithmetic switch { true => "yes", false => "no", _ => "not available" }}; invented way to reply hit by a blocking finding: {(contactHit ? "yes" : "no")}";
        return GateResult.Of(name, EvalBasics.Dev, totalHit || byArithmetic is not null, totalHit && contactHit, detail);
    }

    private static bool Reported(FactCheckItem item, SourceError error, FactFinding finding, FactCheckKit kit)
    {
        var expected = new[] { error.Quote, error.Quote2 }.OfType<string>().ToList();
        var reported = new[] { finding.MaterialQuote, finding.MaterialQuote2 }.OfType<string>().ToList();
        foreach (var file in item.Basis.Files ?? [])
        {
            var wanted = expected.Select(q => kit.Locate(file.Text, q)).OfType<(int Start, int Length)>().ToList();
            var got = reported.Select(q => kit.Locate(file.Text, q)).OfType<(int Start, int Length)>().ToList();
            if (wanted.Any(w => got.Any(g => Overlap(w, g))))
                return true;
        }
        return false;
    }

    private static bool Overlap((int Start, int Length) a, (int Start, int Length) b) =>
        a.Start < b.Start + b.Length && b.Start < a.Start + a.Length;

    private static string? Hash(FactCheckItem item, FactCheckKit kit, Dictionary<string, string> cache)
    {
        if (!cache.TryGetValue(item.Id, out var hash))
            cache[item.Id] = hash = kit.InputHash(item);
        return hash;
    }
}
