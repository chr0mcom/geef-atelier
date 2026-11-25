using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.ResearchEval;
using static Geef.Atelier.Tests.ResearchEvalTool.ResearchEvalTestData;

namespace Geef.Atelier.Tests.ResearchEvalTool;

/// <summary>
/// Gate VR (contract stage 4, 3.3 and 8): passes only with complete, current, independently checked data; one entry not found word for
/// word fails it whatever else is missing; every gap in the data makes it not evaluable instead of passed.
/// </summary>
public sealed class ResearchEvaluationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly IReadOnlyList<ResearchOrder> AllOrders = SyntheticOrders();

    [Fact]
    public async Task Complete_current_and_checked_data_passes_every_part()
    {
        var data = await MeasureAsync();

        var (verdicts, figures) = Evaluate(data);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.Passed, v.Verdict));
        Assert.Equal(["K1 word for word", "validity", "yield", "relevance", "median duration", "overall"], verdicts.Select(v => v.Scope));
        Assert.Contains(figures, f => f.StartsWith("held-out: 9 Found", StringComparison.Ordinal));
        Assert.Contains(figures, f => f.StartsWith("development (no verdict): 12 lines", StringComparison.Ordinal));
        Assert.Contains(figures, f => f.Contains("NotInPage 9", StringComparison.Ordinal));
        Assert.Equal(0, ResearchEvaluation.ExitCode(verdicts[^1].Verdict));
    }

    [Fact]
    public void Without_data_the_gate_is_never_passed()
    {
        var empty = new RunFile("holdout.jsonl", "00", []);

        var (verdicts, _) = ResearchEvaluation.Evaluate(AllOrders, empty, [], [], null, Kit, Settings);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
        Assert.Equal(4, ResearchEvaluation.ExitCode(verdicts[^1].Verdict));
    }

    [Fact]
    public async Task A_missing_or_stale_independent_check_makes_the_gate_not_evaluable()
    {
        var data = await MeasureAsync();

        var missing = Evaluate(data with { Verification = null }).Verdicts;
        var otherFiles = Evaluate(data with { Verification = data.Verification! with { Files = [data.Verification.Files[0] with { Sha256 = "ff" }] } }).Verdicts;
        var otherCount = Evaluate(data with { Verification = data.Verification! with { Entries = data.Verification.Entries - 1 } }).Verdicts;

        Assert.All(new[] { missing, otherFiles, otherCount }, v =>
        {
            Assert.Equal(GateVerdict.NotEvaluable, v[0].Verdict);
            Assert.Equal(GateVerdict.NotEvaluable, v[^1].Verdict);
        });
    }

    [Fact]
    public async Task One_violation_of_the_independent_check_fails_the_gate_even_when_everything_else_is_missing()
    {
        var data = await MeasureAsync();
        var violation = new Violation("holdout.jsonl", 1, "h-01", "W1.1", "not_in_page");

        var full = Evaluate(data with { Verification = data.Verification! with { Violations = [violation] } }).Verdicts;
        var bare = ResearchEvaluation.Evaluate(AllOrders, new RunFile("h", "00", []), [], [], new VerificationReport(ResearchEvaluation.VerifierName, 1, [], 0, [violation]), Kit, Settings).Verdicts;

        Assert.Equal(GateVerdict.Failed, full[0].Verdict);
        Assert.Equal(GateVerdict.Failed, full[^1].Verdict);
        Assert.Equal(GateVerdict.Failed, bare[^1].Verdict);
        Assert.Equal(1, ResearchEvaluation.ExitCode(bare[^1].Verdict));
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("start")]
    [InlineData("sha")]
    [InlineData("url")]
    public async Task The_own_recheck_finds_a_manipulated_entry_although_the_report_is_clean(string field)
    {
        var data = await MeasureAsync();
        var line = data.Holdout.Lines[0];
        var entry = line.Entries[0];
        var changed = field switch
        {
            "quote" => entry with { Quote = entry.Quote.Replace("forty two", "fifty two", StringComparison.Ordinal) },
            "start" => entry with { Start = entry.Start + 1 },
            "sha" => entry with { RawSha256 = JsonLines.Sha256("other") },
            _ => entry with { Url = "https://other.example/" },
        };
        var lines = data.Holdout.Lines.Select(l => l == line ? l with { Entries = [changed, .. l.Entries.Skip(1)] } : l).ToList();

        var (violations, _, _) = ResearchEvaluation.Recheck([data.Holdout with { Lines = lines }]);
        var verdicts = Evaluate(data with { Holdout = data.Holdout with { Lines = lines } }).Verdicts;

        Assert.Single(violations);
        Assert.Equal(GateVerdict.Failed, verdicts[0].Verdict);
        Assert.Equal(GateVerdict.Failed, verdicts[^1].Verdict);
    }

    [Fact]
    public void White_space_may_differ_but_case_may_not()
    {
        var raw = "Der Mindestlohn steigt\n  auf 13,90 Euro je Stunde.";
        var sha = JsonLines.Sha256(raw);
        var page = new PageRecord("P1", 1, "https://a.example/", "A", raw, sha);
        RunFile Of(string quote) => new("f", "00", [Bare("h-01") with
        {
            Pages = [page],
            Entries = [new EntryRecord("W1.1", 1, quote, page.Url, "A", DateTimeOffset.UnixEpoch, "Normalized", sha, 0, raw.Length)],
        }]);

        Assert.Empty(ResearchEvaluation.Recheck([Of("Der Mindestlohn steigt auf 13,90 Euro je Stunde.")]).Violations);
        Assert.Single(ResearchEvaluation.Recheck([Of("der Mindestlohn steigt auf 13,90 Euro je Stunde.")]).Violations);
        var missing = ResearchEvaluation.Recheck([new RunFile("f", "00", [Bare("h-01") with { Entries = Of("x").Lines[0].Entries }])]);
        Assert.Single(missing.MissingRaw);
    }

    [Fact]
    public async Task A_held_out_order_measured_twice_or_for_another_input_is_not_evaluable()
    {
        var data = await MeasureAsync();

        var twice = Evaluate(data with { Also = [.. data.Also, data.Holdout with { Path = "rerun.jsonl", Sha256 = "11" }] }).Verdicts;
        var changed = ResearchEvaluation.Evaluate(AllOrders, data.Holdout, data.Also, data.Judgments, data.Verification, Kit with { SystemPrompt = "changed" }, Settings).Verdicts;
        var withoutHash = Evaluate(data with { Holdout = data.Holdout with { Lines = [data.Holdout.Lines[0] with { InputHash = null }, .. data.Holdout.Lines.Skip(1)] } }).Verdicts;

        Assert.Equal(GateVerdict.NotEvaluable, twice[^1].Verdict);
        Assert.Contains("measured 2 times", twice[1].Detail, StringComparison.Ordinal);
        Assert.Equal(GateVerdict.NotEvaluable, changed[^1].Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, withoutHash[^1].Verdict);
        Assert.Contains("without input fingerprint", withoutHash[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fewer_than_seven_valid_answers_of_the_picker_make_the_gate_not_evaluable()
    {
        var data = await MeasureAsync(condenseError: t => t[0].Contains("h-01", StringComparison.Ordinal) || t[0].Contains("h-02", StringComparison.Ordinal) || t[0].Contains("h-03", StringComparison.Ordinal) ? "condense_failed" : null);

        var verdicts = Evaluate(data).Verdicts;

        Assert.Equal(GateVerdict.NotEvaluable, verdicts[^1].Verdict);
        Assert.Contains("only 6 of 9", verdicts[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_invalid_answer_of_nine_fails_validity()
    {
        var data = await MeasureAsync(condenseError: t => t[0].Contains("h-05", StringComparison.Ordinal) ? "condense_failed" : null);

        var verdicts = Evaluate(data).Verdicts;

        Assert.Equal(GateVerdict.Failed, verdicts.Single(v => v.Scope == "validity").Verdict);
        Assert.Equal(GateVerdict.Passed, verdicts.Single(v => v.Scope == "yield").Verdict);
        Assert.Equal(GateVerdict.Failed, verdicts[^1].Verdict);
    }

    [Fact]
    public async Task Too_few_orders_with_entries_fail_the_yield()
    {
        var data = await MeasureAsync();
        var empty = new HashSet<string>(["h-01", "h-02", "h-03"]);
        var lines = data.Holdout.Lines.Select(l => empty.Contains(l.Id)
            ? l with { Status = nameof(ResearchStatus.NothingFound), Entries = [], Condense = l.Condense! with { Proposed = [] } }
            : l).ToList();
        var holdout = data.Holdout with { Lines = lines };

        var verdicts = Evaluate(data with { Holdout = holdout, Verification = Verify([holdout, .. data.Also]) }).Verdicts;

        var yield = verdicts.Single(v => v.Scope == "yield");
        Assert.Equal(GateVerdict.Failed, yield.Verdict);
        Assert.StartsWith("6/9", yield.Detail, StringComparison.Ordinal);
        Assert.Equal(GateVerdict.Passed, verdicts.Single(v => v.Scope == "relevance").Verdict);
    }

    [Fact]
    public async Task Relevance_needs_a_current_judgement_for_every_entry_and_eighty_percent_help()
    {
        var data = await MeasureAsync();
        var half = data.Judgments.Select(j => j with { Verdicts = j.Verdicts.Select((v, i) => v with { Helps = i == 0 }).ToList() }).ToList();
        var stale = data.Judgments.Skip(1).Append(data.Judgments[0] with { RunHash = "other" }).ToList();

        Assert.Equal(GateVerdict.Failed, Evaluate(data with { Judgments = half }).Verdicts.Single(v => v.Scope == "relevance").Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, Evaluate(data with { Judgments = stale }).Verdicts.Single(v => v.Scope == "relevance").Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, Evaluate(data with { Judgments = data.Judgments.Select(j => j with { Status = RelevanceJudge.Invalid }).ToList() }).Verdicts[^1].Verdict);
    }

    [Fact]
    public async Task The_median_counts_only_real_durations_and_fails_over_sixty_seconds()
    {
        var data = await MeasureAsync(searchError: q => q.Contains("h-08", StringComparison.Ordinal) || q.Contains("h-09", StringComparison.Ordinal) ? "http_500" : null);
        var slow = data.Holdout with { Lines = data.Holdout.Lines.Select(l => l with { DurationMs = l.ErrorCode is null ? 70_000 : 5 }).ToList() };

        var median = Evaluate(data with { Holdout = slow, Verification = Verify([slow, .. data.Also]) }).Verdicts.Single(v => v.Scope == "median duration");

        Assert.Equal(GateVerdict.Failed, median.Verdict);
        Assert.Contains("median 70.0 s over 7 orders", median.Detail, StringComparison.Ordinal);
        Assert.Contains("2 orders without such a duration", median.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_production_code_or_orders_outside_VR_make_the_gate_not_evaluable()
    {
        var data = await MeasureAsync();
        var otherBuilder = Kit with
        {
            Build = (proposed, pages, request, max, at) =>
            {
                var (entries, dropped) = Kit.Build(proposed, pages, request, max, at);
                return (entries.Select(e => e with { Length = e.Length - 1 }).ToList(), dropped);
            },
        };
        var otherGate = Kit with { Select = (topics, request, files, profile, max) => new QuerySelection(topics.Take(1).ToList(), 0, topics.Count - 1) };
        var reserved = data.Holdout.Lines[0] with { Id = "d-20", Set = ResearchCorpus.DevSet };

        var builder = ResearchEvaluation.Evaluate(AllOrders, data.Holdout, data.Also, data.Judgments, data.Verification, otherBuilder, Settings).Verdicts;
        var gate = ResearchEvaluation.Evaluate(AllOrders, data.Holdout, data.Also, data.Judgments, data.Verification, otherGate, Settings).Verdicts;
        var foreign = Evaluate(data with { Also = [.. data.Also, new RunFile("r3.jsonl", "22", [reserved])], Verification = Verify([data.Holdout, .. data.Also, new RunFile("r3.jsonl", "22", [reserved])]) }).Verdicts;

        Assert.Contains("dossier builder now builds other entries", builder[1].Detail, StringComparison.Ordinal);
        Assert.Contains("query gate now sends other topics", gate[1].Detail, StringComparison.Ordinal);
        Assert.Contains("reserved for R3", foreign[1].Detail, StringComparison.Ordinal);
        Assert.All(new[] { builder, gate, foreign }, v => Assert.Equal(GateVerdict.NotEvaluable, v[^1].Verdict));
    }

    private sealed record Measured(RunFile Holdout, IReadOnlyList<RunFile> Also, IReadOnlyList<JudgeLine> Judgments, VerificationReport? Verification);

    private static (IReadOnlyList<GateResult> Verdicts, IReadOnlyList<string> Figures) Evaluate(Measured data) =>
        ResearchEvaluation.Evaluate(AllOrders, data.Holdout, data.Also, data.Judgments, data.Verification, Kit, Settings);

    private async Task<Measured> MeasureAsync(Func<string, string?>? searchError = null, Func<IReadOnlyList<string>, string?>? condenseError = null)
    {
        var holdoutPath = _temp.File("holdout.jsonl");
        var devPath = _temp.File("dev.jsonl");
        await Runner(holdoutPath, ResearchCorpus.HoldoutSet, AllOrders, Transport(), searchError: searchError, condenseError: condenseError).RunAsync(CancellationToken.None);
        await Runner(devPath, ResearchCorpus.DevSet, AllOrders, Transport()).RunAsync(CancellationToken.None);
        var holdout = new RunFile(holdoutPath, JsonLines.FileSha256(holdoutPath), JsonLines.Load<ResearchRunLine>(holdoutPath));
        var dev = new RunFile(devPath, JsonLines.FileSha256(devPath), JsonLines.Load<ResearchRunLine>(devPath));
        var judgments = holdout.Lines.Where(l => l.Entries.Count > 0)
            .Select(l => new JudgeLine(l.Id, RelevanceJudge.RunHash(l), RelevanceJudge.Ok, "claude-opus-5", 1_000, null, "{}",
                l.Entries.Select((_, i) => new PassageVerdict(i + 1, true)).ToList(), RelevanceJudge.InputHash(l), DateTimeOffset.UnixEpoch))
            .ToList();
        return new Measured(holdout, [dev], judgments, Verify([holdout, dev]));
    }

    private static VerificationReport Verify(IReadOnlyList<RunFile> files) =>
        new(ResearchEvaluation.VerifierName, 1, files.Select(f => new VerifiedFile(f.Path, f.Sha256, f.Lines.Count, f.Lines.Sum(l => l.Entries.Count))).ToList(),
            files.Sum(f => f.Lines.Sum(l => l.Entries.Count)), []);

    private static ResearchRunLine Bare(string id) =>
        new(id, ResearchCorpus.HoldoutSet, "Found", null, [], [], 0, 0, 0, 0, [], [], null, [], new Dictionary<string, int>(), 0, 0,
            DateTimeOffset.UnixEpoch, Settings, "h", DateTimeOffset.UnixEpoch);
}
