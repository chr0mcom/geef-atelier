using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Tools.FactCheckEval;
using static Geef.Atelier.Tests.FactCheck.FactCheckTestData;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>Gate V2 never passes without data, counts every attempt and judges hits and false alarms only on answered texts.</summary>
public sealed class FactCheckEvaluationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(180);

    private static readonly IReadOnlyList<CorpusBasis> Corpus = Bases();

    private static IReadOnlyList<FactCheckItem> Items => FactCheckCorpus.Items(Corpus);

    private static FactCheckItem Evening(string variant) => Items.Single(i => i.Basis.Case == FactCheckCorpus.ParentsEvening && i.Variant == variant);

    private static List<FactCheckLine> GoodRun(FactCheckKit kit) =>
        Items.Where(i => i.Split == EvalBasics.Holdout || i.Basis.Case == FactCheckCorpus.ParentsEvening)
            .Select(i => Line(i, kit, raw: i.IsFaulty ? Answer(("unsupported", i.Errors[0].Wording, null)) : Answer()))
            .ToList();

    private static GateResult Gate(IReadOnlyList<GateResult> verdicts, string part) => verdicts.Single(v => v.Gate == $"{FactCheckEvaluation.Gate} {part}");

    [Fact]
    public void Without_results_every_part_is_not_evaluable()
    {
        var verdicts = FactCheckEvaluation.Verdicts(Corpus, [], Kit(), Limit);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
        Assert.Contains("no result", verdicts[^1].Detail);
    }

    [Fact]
    public void Without_corpus_every_part_is_not_evaluable()
    {
        var verdicts = FactCheckEvaluation.Verdicts([], [], Kit(), Limit);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
        Assert.Contains("0 of 16 bases", verdicts[^1].Detail);
    }

    [Fact]
    public void A_good_run_passes_and_the_parents_evening_is_found_through_question_c()
    {
        var kit = Kit();
        var lines = GoodRun(kit);
        var evening = Evening("planted");
        lines.RemoveAll(l => l.Id == evening.Id);
        lines.Add(Line(evening, kit, raw: Answer(("unsupported", ContactSentence, null), ("source_conflict", null, WrongTotalLine))));

        var verdicts = FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.Passed, v.Verdict));
    }

    [Fact]
    public void A_result_for_another_input_or_without_hash_makes_the_file_not_evaluable()
    {
        var kit = Kit();
        var lines = GoodRun(kit);
        lines[0] = lines[0] with { InputHash = "0000" };

        Assert.All(FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit), v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
        Assert.Contains(FactCheckEvaluation.DataProblems(Corpus, lines, kit), p => p.Contains("another input", StringComparison.Ordinal));
        lines[0] = lines[0] with { InputHash = null };
        Assert.Contains(FactCheckEvaluation.DataProblems(Corpus, lines, kit), p => p.Contains("another input", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_held_out_text_or_an_unknown_id_makes_the_file_not_evaluable()
    {
        var kit = Kit();
        var missing = GoodRun(kit).Skip(1).ToList();
        var unknown = GoodRun(kit).Append(Line(Items[0], kit) with { Id = "fc-99/base" }).ToList();

        Assert.Equal(GateVerdict.NotEvaluable, FactCheckEvaluation.Verdicts(Corpus, missing, kit, Limit)[^1].Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, FactCheckEvaluation.Verdicts(Corpus, unknown, kit, Limit)[^1].Verdict);
    }

    [Fact]
    public void An_incomplete_corpus_is_not_evaluable()
    {
        var unlabelled = Corpus.Select((b, i) => i == 0 ? b with { Labels = null } : b).ToList();
        var shorter = Corpus.Skip(1).ToList();

        Assert.Contains(FactCheckCorpus.Problems(unlabelled), p => p.Contains("not labelled", StringComparison.Ordinal));
        Assert.Contains(FactCheckCorpus.Problems(shorter), p => p.Contains("15 of 16", StringComparison.Ordinal));
    }

    [Fact]
    public void Texts_without_an_answer_never_count_as_quiet_and_too_few_answered_texts_are_not_evaluable()
    {
        var kit = Kit();
        var lines = GoodRun(kit).Select(l => l.Id.EndsWith("/base", StringComparison.Ordinal) && l.Split == EvalBasics.Holdout
            ? l with { Status = CallStatus.Failed, Raw = null, ErrorCode = "http_503" }
            : l).ToList();

        var verdicts = FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit);

        Assert.Equal(GateVerdict.NotEvaluable, Gate(verdicts, "false alarms").Verdict);
        Assert.Contains("0 clean", Gate(verdicts, "false alarms").Detail);
    }

    [Fact]
    public void Repeating_a_failed_call_never_raises_the_validity_and_fast_failures_never_lower_the_latency()
    {
        var kit = Kit();
        var lines = GoodRun(kit).Select(l => l with { DurationMs = 150_000 }).ToList();
        var first = lines.First(l => l.Split == EvalBasics.Holdout);
        var answers = lines.Count(l => l.Split == EvalBasics.Holdout) + 10;
        lines.AddRange(Enumerable.Repeat(first, 10));
        lines.Insert(0, first with { Status = CallStatus.Failed, Raw = null, ErrorCode = "http_502", DurationMs = 200 });

        var verdicts = FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit);
        var holdout = FactCheckEvaluation.Calls(FactCheckEvaluation.Replay(Corpus, lines, kit).Where(r => r.Item.Split == EvalBasics.Holdout).ToList(), Limit);

        Assert.Equal(11, holdout.Retries);
        Assert.Equal(answers, holdout.Timed);
        Assert.Equal((double)answers / (answers + 1), holdout.Validity, 6);
        Assert.Equal(150_000, holdout.MedianMs);
        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "latency").Verdict);
    }

    [Fact]
    public void Time_outs_count_in_the_latency_and_an_answer_over_the_production_limit_is_invalid()
    {
        var kit = Kit();
        var lines = GoodRun(kit);
        var holdout = lines.Where(l => l.Split == EvalBasics.Holdout).ToList();
        lines.Add(holdout[0] with { Status = CallStatus.TimedOut, Raw = null, ErrorCode = "timeout", DurationMs = 180_000 });
        var slow = lines.IndexOf(holdout[1]);
        lines[slow] = lines[slow] with { DurationMs = 200_000 };

        var calls = FactCheckEvaluation.Calls(FactCheckEvaluation.Replay(Corpus, lines, kit).Where(r => r.Item.Split == EvalBasics.Holdout).ToList(), Limit);

        Assert.Equal(holdout.Count + 1, calls.Timed);
        Assert.Equal(1, calls.OverProductionLimit);
        Assert.Equal(holdout.Count - 1, calls.Valid);
    }

    [Fact]
    public void Validity_below_ninety_percent_is_not_evaluable_and_below_ninety_five_fails()
    {
        var kit = Kit();
        var good = GoodRun(kit);
        var holdout = good.Where(l => l.Split == EvalBasics.Holdout).ToList();
        var unreadable = holdout[0] with { Raw = "unreadable" };
        var half = good.Concat(Enumerable.Repeat(unreadable, holdout.Count)).ToList();
        var eighteenOfNineteen = good.Concat(Enumerable.Repeat(holdout[1], 18 - holdout.Count)).Append(unreadable).ToList();
        var nineteenOfTwenty = eighteenOfNineteen.Append(holdout[1]).ToList();

        Assert.Equal(GateVerdict.NotEvaluable, Gate(FactCheckEvaluation.Verdicts(Corpus, half, kit, Limit), "validity").Verdict);
        Assert.Equal(GateVerdict.Failed, Gate(FactCheckEvaluation.Verdicts(Corpus, eighteenOfNineteen, kit, Limit), "validity").Verdict);
        Assert.Equal(GateVerdict.Passed, Gate(FactCheckEvaluation.Verdicts(Corpus, nineteenOfTwenty, kit, Limit), "validity").Verdict);
    }

    [Fact]
    public void Blocking_findings_on_clean_texts_are_false_alarms()
    {
        var kit = Kit();
        var lines = GoodRun(kit).Select(l => l.Id.EndsWith("/base", StringComparison.Ordinal) && l.Split == EvalBasics.Holdout
            ? l with { Raw = Answer(("unsupported", "der Ausflug ist am Dienstag", null)) }
            : l).ToList();

        var verdict = Gate(FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit), "false alarms");

        Assert.Equal(GateVerdict.Failed, verdict.Verdict);
        Assert.Contains("blocking findings per clean text", verdict.Detail);
    }

    [Fact]
    public void A_finding_elsewhere_in_the_text_is_no_hit_and_source_conflicts_on_plain_texts_count_against_question_c()
    {
        var kit = Kit();
        var lines = GoodRun(kit).Select(l => l.Split == EvalBasics.Holdout
            ? l with { Raw = Answer(("unsupported", "Bitte zahlen Sie bis Ende April.", null), ("source_conflict", null, WrongTotalLine)) }
            : l).ToList();

        var verdicts = FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit);

        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "hit rate").Verdict);
        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "source conflicts").Verdict);
        Assert.Equal(GateVerdict.Failed, verdicts[^1].Verdict);
    }

    [Fact]
    public void The_parents_evening_needs_the_wrong_total_and_the_invented_way_to_reply()
    {
        var evening = Evening("planted");
        FactCheckLine EveningLine(FactCheckKit kit, string raw) => Line(evening, kit, raw: raw);
        GateResult Judge(FactCheckKit kit, string raw)
        {
            var lines = GoodRun(kit).Where(l => l.Id != evening.Id).Append(EveningLine(kit, raw)).ToList();
            return Gate(FactCheckEvaluation.Verdicts(Corpus, lines, kit, Limit), "parents' evening");
        }

        var onlyContact = Answer(("unsupported", ContactSentence, null));
        var both = Answer(("unsupported", ContactSentence, null), ("source_conflict", null, WrongTotalLine));
        var arithmetic = Kit(_ => new SourceArithmeticResult(1, [new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, WrongTotalLine, ["9 €", "6 €", "4 €"], "17 €", "19 €", TotalUseInText.Neither)]));
        var arithmeticQuiet = Kit(_ => new SourceArithmeticResult(1, []));

        Assert.Equal(GateVerdict.NotEvaluable, Judge(Kit(), onlyContact).Verdict);
        Assert.Equal(GateVerdict.Passed, Judge(Kit(), both).Verdict);
        Assert.Equal(GateVerdict.Passed, Judge(arithmetic, onlyContact).Verdict);
        Assert.Equal(GateVerdict.Failed, Judge(arithmeticQuiet, onlyContact).Verdict);
        Assert.Equal(GateVerdict.Failed, Judge(Kit(), Answer(("source_conflict", null, WrongTotalLine))).Verdict);
    }

    [Fact]
    public void Answered_skips_only_texts_whose_last_result_for_the_current_input_is_an_answer()
    {
        var kit = Kit();
        var items = Items.Take(3).ToList();
        var hashes = items.ToDictionary(i => i.Id, kit.InputHash);
        var lines = new[]
        {
            Line(items[0], kit),
            Line(items[1], kit, status: CallStatus.Failed),
            Line(items[2], kit, hash: "stale"),
        };

        Assert.Equal([items[0].Id], FactCheckEvaluation.Answered(lines, hashes));
    }

    [Fact]
    public void The_input_hash_covers_prompt_schema_material_and_text()
    {
        var kit = Kit();
        var item = Items[0];
        var hash = kit.InputHash(item);

        Assert.NotEqual(hash, (kit with { SystemPrompt = "other" }).InputHash(item));
        Assert.NotEqual(hash, (kit with { SchemaJson = "{\"x\":1}" }).InputHash(item));
        Assert.NotEqual(hash, kit.InputHash(item with { Text = item.Text + "." }));
        Assert.NotEqual(hash, kit.InputHash(item with { Basis = item.Basis with { Request = "anders" } }));
    }

    [Fact]
    public void The_planted_copy_also_expects_the_labels_that_are_still_in_it()
    {
        var basis = Corpus[0] with { Labels = [new CorpusLabel("Bitte zahlen Sie bis Ende April.", null)] };

        var items = FactCheckCorpus.Items(basis);

        Assert.True(items[0].IsFaulty);
        Assert.Equal(["changed_date", "labelled"], items[1].Errors.Select(e => e.Kind));
    }
}
