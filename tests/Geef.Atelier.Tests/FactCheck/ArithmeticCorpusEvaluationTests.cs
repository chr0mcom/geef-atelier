using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>V3: traces are recomputed by the code, and the gate is not evaluable without a complete corpus or a working check.</summary>
public sealed class ArithmeticCorpusEvaluationTests
{
    private static ArithmeticEntry Entry(int i, bool planted, string? form = null)
    {
        if (form == "none")
            return new ArithmeticEntry($"ar-{i:00}", "de", "none", $"Treffen {i} am 3. Mai um 14 Uhr, Hausnummer 12.", false, null);
        var stated = planted ? "62" : "72";
        var line = $"Summe: {stated} €";
        return new ArithmeticEntry($"ar-{i:00}", "de", "list", $"Posten {i}\nBus 22 €\nEintritt 31 €\nEssen 19 €\n{line}", planted,
            new ArithmeticTrace(line, ["22 €", "31 €", "19 €"], [], stated, "72"));
    }

    private static IReadOnlyList<ArithmeticEntry> Corpus() =>
        Enumerable.Range(1, 60).Select(i => i % 2 == 0 ? Entry(i, true) : Entry(i, false, i % 4 == 1 ? "none" : null)).ToList();

    private static SourceArithmeticResult Perfect(IReadOnlyList<(string SourceId, string FileName, string Text)> sources) =>
        new(1, sources[0].Text.Contains("62 €", StringComparison.Ordinal)
            ? [new SourceArithmeticIssue("S1", sources[0].FileName, ArithmeticForm.ListTotal, "Summe: 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither)]
            : []);

    [Fact]
    public void Traces_are_recomputed_with_operators()
    {
        Assert.Equal(72m, ArithmeticCorpusEvaluation.Recalculate(new ArithmeticTrace("x", ["22", "31", "19"], [], "72", "72")));
        Assert.Equal(82m, ArithmeticCorpusEvaluation.Recalculate(new ArithmeticTrace("x", ["10", "3", "24 €"], ["+", "×"], "82", "82")));
        Assert.Null(ArithmeticCorpusEvaluation.Recalculate(new ArithmeticTrace("x", ["10", "3"], ["/"], "1", "1")));
        Assert.Null(ArithmeticCorpusEvaluation.TraceProblem(Entry(2, true)));
        Assert.Contains("stated total is right", ArithmeticCorpusEvaluation.TraceProblem(Entry(3, false) with { Planted = true }));
        Assert.Contains("stated total is wrong", ArithmeticCorpusEvaluation.TraceProblem(Entry(2, true) with { Planted = false }));
        Assert.Contains("not in the text", ArithmeticCorpusEvaluation.TraceProblem(Entry(2, true) with { Text = "Bus 22 €" }));
    }

    [Fact]
    public void A_perfect_check_passes_and_the_development_set_never_gets_a_verdict()
    {
        var verdicts = ArithmeticCorpusEvaluation.Verdicts(Corpus(), Perfect);

        Assert.Equal(GateVerdict.Passed, verdicts.Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, verdicts.Single(v => v.Scope == EvalBasics.Dev).Verdict);
    }

    [Fact]
    public void Missed_totals_and_false_alarms_fail()
    {
        var silent = ArithmeticCorpusEvaluation.Verdicts(Corpus(), _ => new SourceArithmeticResult(0, []));
        var noisy = ArithmeticCorpusEvaluation.Verdicts(Corpus(), s => new SourceArithmeticResult(1,
            [new SourceArithmeticIssue("S1", "x", ArithmeticForm.ListTotal, "Summe", ["22 €"], "62 €", "72 €", TotalUseInText.Neither)]));

        Assert.Equal(GateVerdict.Failed, silent.Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
        Assert.Equal(GateVerdict.Failed, noisy.Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
    }

    [Fact]
    public void An_incomplete_corpus_a_broken_trace_or_a_missing_check_is_not_evaluable()
    {
        var corpus = Corpus();
        var broken = corpus.Select((e, i) => i == 1 ? e with { Trace = e.Trace! with { Correct = "70" } } : e).ToList();

        Assert.Equal(GateVerdict.NotEvaluable, ArithmeticCorpusEvaluation.Verdicts([], Perfect).Single().Verdict);
        Assert.Equal(GateVerdict.NotEvaluable, ArithmeticCorpusEvaluation.Verdicts(corpus.Skip(1).ToList(), Perfect).Single().Verdict);
        Assert.Contains("traces do not hold", ArithmeticCorpusEvaluation.Verdicts(broken, Perfect).Single().Detail);
        Assert.Equal(GateVerdict.NotEvaluable, ArithmeticCorpusEvaluation.Verdicts(corpus, _ => throw new NotImplementedException())
            .Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
    }
}
