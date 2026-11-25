using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Infrastructure.Intake;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>Outcome of one acceptance gate.</summary>
public enum GateVerdict { Passed, Failed, NotEvaluable }

/// <summary>The verdict of gate <see cref="Gate"/> for one part of the data (<see cref="Scope"/>), with the figures behind it.</summary>
public sealed record GateResult(string Gate, string Scope, GateVerdict Verdict, string Detail)
{
    /// <summary>One line for the console: gate, part, verdict and figures.</summary>
    public override string ToString() => $"{Gate,-3} [{Scope}] {Verdict switch
    {
        GateVerdict.Passed => "passed",
        GateVerdict.Failed => "FAILED",
        _ => "NOT EVALUABLE",
    }}: {Detail}";
}

/// <summary>A stored clarifier answer replayed through the current parser and gate; <see cref="Gate"/> is null when it cannot be replayed.</summary>
public sealed record Replay(CorpusEntry Entry, EvalResult Answer, ClarifierOutput? Output, QuestionGateResult? Gate);

/// <summary>
/// Figures over every call line (every attempt, also repeated ones): gates a and f. The latency (<see cref="MedianMs"/>,
/// <see cref="P90Ms"/>) covers only answers (<c>Ok</c>) and time-outs (<see cref="Timed"/> lines), so that fast failures never make
/// it look better; the failure rate covers every line.
/// </summary>
public sealed record CallFigures(int Calls, int Entries, int Retries, int Failures, double FailureRate, int Timed, double MedianMs, double P90Ms, int OverProductionLimit, int Unreplayable);

/// <summary>Figures over the last successful, replayable answer of each entry: gates b to e′.</summary>
public sealed record QuestionFigures(
    int Answered,
    double MeanQuestions,
    int MaxQuestions,
    int CompleteAnswered,
    double CompleteAskedRate,
    int CompleteMaxQuestions,
    int BadDeliveredQuestions,
    double RawQuestionDropRate,
    int Labelled,
    double ForbiddenAskRate,
    int ResearchableAnswered,
    int ResearchableTopicAsked,
    int ProfileCoveredAnswered,
    int ProfileTopicAsked,
    int Necessary,
    double NecessaryHitRate);

/// <summary>
/// Replays the stored answers of <c>IntakeEval run</c> and judges the acceptance gates of contract stage 2, 6.6 for the held-out set
/// and the second corpus. No gate passes without data: missing or unknown entries, results made for another input (see
/// <see cref="InputHash"/>), wrong corpus sizes, unlabelled entries or an empty part make every gate "not evaluable"; an answer that
/// is <c>Ok</c> but cannot be replayed or took longer than the production limit counts as a failure; the failure rate counts every
/// attempt and the latency every answer and time-out, so repeating a run never improves them.
/// </summary>
public static class CorpusEvaluation
{
    /// <summary>Entries of the main corpus (contract).</summary>
    public const int MainSize = 120;

    /// <summary>Entries of the second corpus (contract).</summary>
    public const int SecondSize = 40;

    /// <summary>Gate a: highest median latency in milliseconds.</summary>
    public const double MaxMedianMs = 25_000;

    /// <summary>Gate a: highest 90th percentile latency in milliseconds.</summary>
    public const double MaxP90Ms = 40_000;

    /// <summary>Gate b: highest mean number of questions.</summary>
    public const double MaxMeanQuestions = 1.0;

    /// <summary>Gate c: highest share of complete requests with a question.</summary>
    public const double MaxCompleteAskedRate = 0.05;

    /// <summary>Gate e: highest share of entries with a question on a forbidden topic.</summary>
    public const double MaxForbiddenAskRate = 0.10;

    /// <summary>Gate e′ (warning only): lowest share of necessary topics asked.</summary>
    public const double MinNecessaryHitRate = 0.5;

    /// <summary>Gate f: highest failure rate.</summary>
    public const double MaxFailureRate = 0.05;

    /// <summary>Name of the held-out part of the main corpus.</summary>
    public const string HeldOut = "held-out";

    /// <summary>Name of the second corpus.</summary>
    public const string Second = "second";

    private static readonly HashSet<GateDecision> FormDrops =
        [GateDecision.DroppedTooLong, GateDecision.DroppedBadOptions, GateDecision.DroppedJargon, GateDecision.DroppedLanguageMismatch];

    /// <summary>Replays one stored answer with the same mapping as the measuring run.</summary>
    public static Replay ReplayOne(CorpusEntry entry, EvalResult answer)
    {
        if (answer.Status != nameof(IntakeRecordStatus.Ok) || answer.Raw is null)
            return new Replay(entry, answer, null, null);
        var output = ClarifierOutputParser.Parse(answer.Raw);
        if (output is null)
            return new Replay(entry, answer, null, null);
        var options = new IntakeOptions();
        return new Replay(entry, answer, output, entry.Gate(entry.Request(options), output, options).Gate);
    }

    /// <summary>True when the line counts as a failure: not <c>Ok</c>, longer than <paramref name="productionLimit"/> or not replayable.</summary>
    public static bool IsFailure(Replay replay, TimeSpan productionLimit) =>
        replay.Answer.Status != nameof(IntakeRecordStatus.Ok)
        || replay.Answer.DurationMs > productionLimit.TotalMilliseconds
        || replay.Gate is null;

    /// <summary>
    /// Fingerprint of everything the clarifier was given for <paramref name="entry"/>: system prompt, schema and the user prompt
    /// (request, file excerpts, profile, revision note), built with the default <see cref="IntakeOptions"/> like the measuring run.
    /// </summary>
    public static string InputHash(CorpusEntry entry) => entry.InputHash(new IntakeOptions());

    /// <summary>
    /// Ids of the entries that <c>IntakeEval run</c> does not call again: the last result made for the current input of the entry is
    /// <c>Ok</c> or <c>Invalid</c>. Results for another input (changed request, files, profile, prompt or schema) never count.
    /// </summary>
    public static IReadOnlySet<string> Answered(IReadOnlyList<CorpusEntry> corpus, IReadOnlyList<EvalResult> lines)
    {
        var current = corpus.GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        return lines
            .Where(l => current.TryGetValue(l.Id, out var entry) && SameInput(entry, l, hashes))
            .GroupBy(l => l.Id, StringComparer.Ordinal)
            .Where(g => g.Last().Status is nameof(IntakeRecordStatus.Ok) or nameof(IntakeRecordStatus.Invalid))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Lines of <paramref name="lines"/> that were made for another input than the current corpus entry with the same id.</summary>
    public static IReadOnlyList<EvalResult> OtherInput(IReadOnlyList<CorpusEntry> corpus, IReadOnlyList<EvalResult> lines)
    {
        var current = corpus.GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        return lines.Where(l => current.TryGetValue(l.Id, out var entry) && !SameInput(entry, l, hashes)).ToList();
    }

    private static bool SameInput(CorpusEntry entry, EvalResult line, Dictionary<string, string> hashes)
    {
        if (!hashes.TryGetValue(entry.Id, out var hash))
            hashes[entry.Id] = hash = InputHash(entry);
        return string.Equals(line.InputHash, hash, StringComparison.Ordinal)
            && line.Corpus == entry.Corpus && line.Category == entry.Category && line.Lang == entry.Lang && line.Split == entry.Split();
    }

    /// <summary>Gates a and f over every line, retries included; the latency only over answers and time-outs.</summary>
    public static CallFigures Calls(IReadOnlyList<Replay> lines, TimeSpan productionLimit)
    {
        var durations = lines
            .Where(r => r.Answer.Status is nameof(IntakeRecordStatus.Ok) or nameof(IntakeRecordStatus.TimedOut))
            .Select(r => (double)r.Answer.DurationMs)
            .Order()
            .ToList();
        var failures = lines.Count(r => IsFailure(r, productionLimit));
        var entries = lines.Select(r => r.Answer.Id).Distinct(StringComparer.Ordinal).Count();
        return new CallFigures(
            lines.Count,
            entries,
            lines.Count - entries,
            failures,
            lines.Count == 0 ? 0 : (double)failures / lines.Count,
            durations.Count,
            Percentile(durations, 0.5),
            Percentile(durations, 0.9),
            lines.Count(r => r.Answer.Status == nameof(IntakeRecordStatus.Ok) && r.Answer.DurationMs > productionLimit.TotalMilliseconds),
            lines.Count(r => r.Answer.Status == nameof(IntakeRecordStatus.Ok) && r.Gate is null));
    }

    /// <summary>The last successful, replayable answer within the production limit of each entry.</summary>
    public static IReadOnlyList<Replay> LastAnswers(IReadOnlyList<Replay> lines, TimeSpan productionLimit) =>
        lines.Where(r => !IsFailure(r, productionLimit))
            .GroupBy(r => r.Answer.Id, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();

    /// <summary>Gates b to e′ over answered entries only; an entry without an answer never counts as "no question".</summary>
    public static QuestionFigures Questions(IReadOnlyList<Replay> answered)
    {
        var counts = answered.Select(r => r.Gate!.Questions.Count).ToList();
        var complete = answered.Where(r => r.Entry.Category == "complete").ToList();
        var questions = answered.SelectMany(r => r.Gate!.Questions).ToList();
        var rawAsks = answered.Sum(r => r.Output!.Gaps.Count(g => g.Resolution == GapResolution.Ask && g.Topic != GapTopic.Length));
        var formDrops = answered.Sum(r => r.Gate!.Trace.Count(t => FormDrops.Contains(t.Decision)));
        var labelled = answered.Where(r => r.Entry.Labels is { Count: > 0 }).ToList();
        var researchable = labelled.Where(r => r.Entry.Category == "researchable").ToList();
        var profileCovered = labelled.Where(r => r.Entry.Category == "profile_covered").ToList();
        var necessary = labelled.Sum(r => r.Entry.Labels!.Count(l => l.Value == "necessary_ask"));
        var necessaryHits = labelled.Sum(r => r.Entry.Labels!
            .Where(l => l.Value == "necessary_ask")
            .Count(l => r.Gate!.Questions.Any(q => Topic(l.Key) == q.Slot)));

        return new QuestionFigures(
            answered.Count,
            counts.Count == 0 ? 0 : counts.Average(),
            counts.DefaultIfEmpty(0).Max(),
            complete.Count,
            complete.Count == 0 ? 0 : (double)complete.Count(r => r.Gate!.Questions.Count > 0) / complete.Count,
            complete.Select(r => r.Gate!.Questions.Count).DefaultIfEmpty(0).Max(),
            questions.Count(Bad),
            rawAsks == 0 ? 0 : (double)formDrops / rawAsks,
            labelled.Count,
            labelled.Count == 0 ? 0 : (double)labelled.Count(r => AskedWithLabel(r, "forbidden_ask") > 0) / labelled.Count,
            researchable.Count,
            researchable.Sum(r => AskedWithLabel(r, "forbidden_ask")),
            profileCovered.Count,
            profileCovered.Sum(r => AskedWithLabel(r, "forbidden_ask")),
            necessary,
            necessary == 0 ? 0 : (double)necessaryHits / necessary);
    }

    /// <summary>Why the data cannot be judged; empty when it can. Checked for one result file against both corpora.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<CorpusEntry> corpus, IReadOnlyList<EvalResult> lines)
    {
        var problems = new List<string>();
        var main = corpus.Count(e => e.Corpus == "main");
        var second = corpus.Count(e => e.Corpus == Second);
        if (main != MainSize)
            problems.Add($"main corpus has {main} of {MainSize} entries");
        if (second != SecondSize)
            problems.Add($"second corpus has {second} of {SecondSize} entries");
        var unlabelled = corpus.Count(e => e.Labels is not { Count: > 0 });
        if (unlabelled > 0)
            problems.Add($"{unlabelled} entries are not labelled");
        if (!corpus.Any(e => e.Corpus == "main" && e.Split() == "holdout"))
            problems.Add("the held-out set is empty");
        if (second == 0)
            problems.Add("the second corpus is empty");

        var ids = corpus.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var answered = lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var missing = ids.Where(id => !answered.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            problems.Add($"{missing.Count} entries have no result ({string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? ", …" : string.Empty)})");
        var unknown = answered.Where(id => !ids.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            problems.Add($"{unknown.Count} results belong to no corpus entry ({string.Join(", ", unknown.Take(5))}{(unknown.Count > 5 ? ", …" : string.Empty)})");
        var other = OtherInput(corpus, lines).Select(l => l.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (other.Count > 0)
            problems.Add($"{other.Count} entries have results made for another input (request, files, profile, clarifier prompt or schema changed, " +
                $"or no input hash); start a new result file ({string.Join(", ", other.Take(5))}{(other.Count > 5 ? ", …" : string.Empty)})");
        return problems;
    }

    /// <summary>
    /// The verdict of gates a to f (and the warning e′) for the held-out set and the second corpus. With any of
    /// <see cref="Problems"/>, every gate is <see cref="GateVerdict.NotEvaluable"/>.
    /// </summary>
    public static IReadOnlyList<GateResult> Verdicts(IReadOnlyList<CorpusEntry> corpus, IReadOnlyList<EvalResult> lines, TimeSpan productionLimit)
    {
        var problems = Problems(corpus, lines);
        var gates = new[] { "a", "b", "c", "d", "e", "e'", "f" };
        if (problems.Count > 0)
        {
            var reason = string.Join("; ", problems);
            return [.. new[] { HeldOut, Second }.SelectMany(scope => gates.Select(g => new GateResult(g, scope, GateVerdict.NotEvaluable, reason)))];
        }

        var byId = corpus.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var replays = lines.Select(l => ReplayOne(byId[l.Id], l)).ToList();
        var results = new List<GateResult>();
        foreach (var (scope, inScope) in new (string, Func<CorpusEntry, bool>)[]
        {
            (HeldOut, e => e.Corpus == "main" && e.Split() == "holdout"),
            (Second, e => e.Corpus == Second),
        })
        {
            var part = replays.Where(r => inScope(r.Entry)).ToList();
            var entries = corpus.Where(inScope).ToList();
            results.AddRange(Judge(scope, entries, part, productionLimit));
        }
        return results;
    }

    /// <summary>The gates of one part.</summary>
    public static IReadOnlyList<GateResult> Judge(string scope, IReadOnlyList<CorpusEntry> entries, IReadOnlyList<Replay> lines, TimeSpan productionLimit)
    {
        var calls = Calls(lines, productionLimit);
        var answered = LastAnswers(lines, productionLimit);
        var q = Questions(answered);
        var hasResearchable = entries.Any(e => e.Category == "researchable");
        var hasProfileCovered = entries.Any(e => e.Category == "profile_covered");
        var callDetail = $"{calls.Calls} calls for {calls.Entries} entries ({calls.Retries} repeated)";

        GateResult Result(string gate, bool evaluable, bool passed, string detail) =>
            new(gate, scope, !evaluable ? GateVerdict.NotEvaluable : passed ? GateVerdict.Passed : GateVerdict.Failed, detail);

        return
        [
            Result("a", calls.Timed > 0, calls.MedianMs <= MaxMedianMs && calls.P90Ms <= MaxP90Ms,
                $"median {calls.MedianMs / 1000:0.0} s, p90 {calls.P90Ms / 1000:0.0} s over {calls.Timed} answers and time-outs of {callDetail}"),
            Result("b", q.Answered > 0, q.MeanQuestions <= MaxMeanQuestions && q.MaxQuestions <= QuestionGate.HardMaxQuestions,
                $"mean {q.MeanQuestions:0.00} questions, at most {q.MaxQuestions}, over {q.Answered} answered entries"),
            Result("c", q.CompleteAnswered > 0, q.CompleteAskedRate <= MaxCompleteAskedRate && q.CompleteMaxQuestions <= 1,
                $"{q.CompleteAskedRate:P1} of {q.CompleteAnswered} complete requests with a question, at most {q.CompleteMaxQuestions}"),
            Result("d", q.Answered > 0, q.BadDeliveredQuestions == 0,
                $"{q.BadDeliveredQuestions} bad questions delivered; {q.RawQuestionDropRate:P1} of the raw model questions dropped by the form rules"),
            Result("e",
                q.Labelled > 0 && (!hasResearchable || q.ResearchableAnswered > 0) && (!hasProfileCovered || q.ProfileCoveredAnswered > 0),
                q.ForbiddenAskRate <= MaxForbiddenAskRate && q.ResearchableTopicAsked == 0 && q.ProfileTopicAsked == 0,
                $"{q.ForbiddenAskRate:P1} of {q.Labelled} labelled entries ask a forbidden topic; researchable {q.ResearchableTopicAsked} of {q.ResearchableAnswered}, " +
                $"profile covered {q.ProfileTopicAsked} of {q.ProfileCoveredAnswered}"),
            Result("e'", q.Necessary > 0, q.NecessaryHitRate >= MinNecessaryHitRate,
                $"warning only: {q.NecessaryHitRate:P1} of {q.Necessary} necessary topics asked"),
            Result("f", calls.Calls > 0, calls.FailureRate <= MaxFailureRate,
                $"{calls.FailureRate:P1} failures ({calls.Failures}, of them {calls.OverProductionLimit} over the production limit and {calls.Unreplayable} not replayable) over {callDetail}"),
        ];
    }

    private static int AskedWithLabel(Replay r, string level) =>
        r.Gate!.Questions.Count(q => Label(r.Entry, q.Slot) == level);

    private static bool Bad(ClarifyQuestion q) =>
        q.Text.Length is 0 or > QuestionGate.MaxQuestionChars
        || q.Options.Count is < QuestionGate.MinOptions or > QuestionGate.MaxOptions
        || q.Options.Any(o => o.Label.Length is 0 or > QuestionGate.MaxOptionChars)
        || JargonBlocklist.Contains(q.Text) || q.Options.Any(o => JargonBlocklist.Contains(o.Label));

    private static GapTopic? Topic(string key) => Enum.TryParse<GapTopic>(key.Replace("_", string.Empty, StringComparison.Ordinal), true, out var t) ? t : null;

    private static string? Label(CorpusEntry entry, GapTopic topic) =>
        entry.Labels?.FirstOrDefault(l => Topic(l.Key) == topic).Value;

    internal static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        var rank = p * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}
