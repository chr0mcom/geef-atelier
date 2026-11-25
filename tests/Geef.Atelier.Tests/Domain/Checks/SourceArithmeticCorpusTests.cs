using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Tools.FactCheckEval;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Checks;

/// <summary>
/// Replays trial V3 (stage 3 contract, 3.3) without a model: synthetic snippets, half of them with a planted wrong total, run
/// through <see cref="SourceArithmeticCheck"/>. The corpus is split 70/30 by a hash of the snippet into a development set (rules
/// may only be derived from it) and a held-out set that decides the gate. Without the corpus file or with too few snippets the
/// gate is not evaluable, which never counts as passed.
/// </summary>
public sealed class SourceArithmeticCorpusTests(ITestOutputHelper output)
{
    public const string CorpusFile = "arithmetic-corpus.jsonl";
    public const double MinRecall = 0.80;
    public const int CleanPerFalseAlarm = 9;
    public const int MinPerClass = 5;

    public enum Verdict { NotEvaluable = 0, Passed = 1, Failed = 2 }

    /// <summary>The trace of a snippet as the corpus file writes it (<c>trace.stated</c>, <c>trace.correct</c>).</summary>
    public sealed record Trace(
        [property: JsonPropertyName("stated")] string? Stated,
        [property: JsonPropertyName("correct")] string? Correct);

    /// <summary>
    /// A snippet with the field names of <c>arithmetic-corpus.jsonl</c> (the format of <c>FactCheck/Evaluation/ArithmeticCorpus.cs</c>):
    /// <c>planted</c> marks a wrong total, its <c>trace</c> holds the stated and the right total.
    /// </summary>
    public sealed record Item(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("lang")] string? Lang,
        [property: JsonPropertyName("form")] string? Form,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("planted")] bool HasError,
        [property: JsonPropertyName("trace")] Trace? Calculation)
    {
        /// <summary>The stated (wrong) total of a planted snippet.</summary>
        public string? Stated => Calculation?.Stated;
    }

    public sealed record Measurement(int Errors, int Hits, int Clean, int FalseAlarms, IReadOnlyList<Item> Missed, IReadOnlyList<Item> Alarms)
    {
        public double Recall => Errors == 0 ? 0 : (double)Hits / Errors;

        public Verdict Verdict => Errors < MinPerClass || Clean < MinPerClass
            ? Verdict.NotEvaluable
            : Recall >= MinRecall && FalseAlarms * CleanPerFalseAlarm <= Clean ? Verdict.Passed : Verdict.Failed;
    }

    public static string CorpusPath => Path.Combine(AppContext.BaseDirectory, "Domain", "Checks", "Corpus", CorpusFile);

    public static IReadOnlyList<Item>? Load(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<Item>(l)!).ToList()
            : null;

    public static bool InDevelopmentSet(string text) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(text))[0] % 10 < 7;

    public static Measurement Measure(IEnumerable<Item> items) => Measure(items, SourceArithmeticCheck.Check);

    /// <summary>Hits and false alarms of <paramref name="check"/> on <paramref name="items"/>.</summary>
    public static Measurement Measure(IEnumerable<Item> items, Func<IReadOnlyList<(string SourceId, string FileName, string Text)>, SourceArithmeticResult> check)
    {
        int errors = 0, hits = 0, clean = 0, alarms = 0;
        var missed = new List<Item>();
        var alarmed = new List<Item>();
        foreach (var item in items)
        {
            var issues = check([(item.Id ?? "S1", "corpus", item.Text)]).Issues;
            if (item.HasError)
            {
                errors++;
                if (issues.Any(i => item.Stated is null || SameFigure(i.Stated, item.Stated)))
                    hits++;
                else
                    missed.Add(item);
            }
            else
            {
                clean++;
                if (issues.Count > 0)
                {
                    alarms++;
                    alarmed.Add(item);
                }
            }
        }
        return new Measurement(errors, hits, clean, alarms, missed, alarmed);
    }

    public static Verdict Gate(IReadOnlyList<Item>? corpus) =>
        corpus is null ? Verdict.NotEvaluable : Measure(corpus.Where(i => !InDevelopmentSet(i.Text))).Verdict;

    private static bool SameFigure(string left, string right) => Digits(left) == Digits(right);

    private static string Digits(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        return digits.TrimEnd('0');
    }

    private static Item Snippet(string text, bool error, string? stated = null) =>
        new(null, "de", "list", text, error, stated is null ? null : new Trace(stated, null));

    [Fact]
    public void Missing_corpus_is_not_evaluable_and_never_passed()
    {
        Assert.Equal(Verdict.NotEvaluable, Gate(Load(Path.Combine(AppContext.BaseDirectory, "does-not-exist.jsonl"))));
        Assert.Equal(Verdict.NotEvaluable, Gate([]));
    }

    [Fact]
    public void Too_few_snippets_of_a_class_are_not_evaluable()
    {
        var items = Enumerable.Range(0, 4).Select(i => Snippet($"A {i + 1} €\nB 2 €\nSumme {i + 10} €", true))
            .Concat(Enumerable.Range(0, 20).Select(i => Snippet($"A {i + 1} €\nB 2 €\nSumme {i + 3} €", false)));

        Assert.Equal(Verdict.NotEvaluable, Measure(items).Verdict);
    }

    [Fact]
    public void Gate_counts_hits_and_false_alarms()
    {
        var errors = Enumerable.Range(0, 10).Select(i => Snippet($"A {i + 1} €\nB 2 €\nSumme {i + 10} €", true, $"{i + 10} €")).ToList();
        var clean = Enumerable.Range(0, 9).Select(i => Snippet($"A {i + 1} €\nB 2 €\nSumme {i + 3} €", false)).ToList();
        var alarm = Snippet("A 1 €\nB 2 €\nSumme 4 €", false);
        var missed = Snippet("Alles in Ordnung, keine Rechnung.", true, "62 €");

        var passed = Measure(errors.Concat(clean).Append(alarm));
        Assert.Equal(10, passed.Hits);
        Assert.Equal(1, passed.FalseAlarms);
        Assert.Equal(Verdict.Passed, passed.Verdict);

        var failedByAlarms = Measure(errors.Concat(clean.Take(5)).Append(alarm).Append(alarm));
        Assert.Equal(Verdict.Failed, failedByAlarms.Verdict);

        var failedByRecall = Measure(errors.Take(5).Concat(Enumerable.Repeat(missed, 2)).Concat(clean));
        Assert.Equal(5, failedByRecall.Hits);
        Assert.Equal(Verdict.Failed, failedByRecall.Verdict);
    }

    [Fact]
    public void A_hit_must_name_the_planted_total()
    {
        var item = Snippet("A 1 €\nB 2 €\nSumme 4 €\n\nC 5 €\nD 5 €\nSumme 10 €", true, "99 €");

        Assert.Equal(0, Measure([item]).Hits);
    }

    [Fact]
    public void Corpus_replay_for_both_sets()
    {
        var corpus = Load(CorpusPath);
        if (corpus is null)
        {
            output.WriteLine($"{CorpusFile} is missing: trial V3 is not evaluable (never passed).");
            Assert.Equal(Verdict.NotEvaluable, Gate(corpus));
            return;
        }

        Assert.All(corpus, i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
        Assert.All(corpus.Where(i => i.HasError), i => Assert.False(string.IsNullOrWhiteSpace(i.Stated)));
        foreach (var (name, development) in new[] { ("development set", true), ("held-out set", false) })
        {
            var m = Measure(corpus.Where(i => InDevelopmentSet(i.Text) == development));
            output.WriteLine($"{name}: recall {m.Hits}/{m.Errors} ({m.Recall:P0}), false alarms {m.FalseAlarms}/{m.Clean}, verdict {m.Verdict}");
            foreach (var item in m.Missed.Concat(m.Alarms))
                output.WriteLine($"  {(item.HasError ? "missed" : "false alarm")} [{item.Id} {item.Form} {item.Lang}]: {item.Text.Replace('\n', '⏎')}");
        }

        Assert.Equal(ArithmeticCorpusEvaluation.ExpectedPlanted, corpus.Count(i => i.HasError));
        Assert.Equal(ArithmeticCorpusEvaluation.ExpectedEntries - ArithmeticCorpusEvaluation.ExpectedPlanted, corpus.Count(i => !i.HasError));
        Assert.True(Gate(corpus) == Verdict.Passed,
            "Trial V3 did not pass on the held-out set: improve the rules on the development set only, or ship Direct:SourceArithmetic=false.");

        var entries = ArithmeticCorpusEvaluation.Load(CorpusPath);
        var verdicts = ArithmeticCorpusEvaluation.Verdicts(entries, SourceArithmeticCheck.Check);
        foreach (var verdict in verdicts)
            output.WriteLine(verdict.ToString());
        Assert.Equal(GateVerdict.Passed, verdicts.Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
    }

    [Fact]
    public void A_check_that_finds_nothing_fails_on_the_real_corpus()
    {
        var corpus = Load(CorpusPath);
        Assert.NotNull(corpus);
        SourceArithmeticResult Silent(IReadOnlyList<(string SourceId, string FileName, string Text)> sources) => new(0, []);

        Assert.Equal(Verdict.Failed, Measure(corpus.Where(i => !InDevelopmentSet(i.Text)), Silent).Verdict);
        Assert.Equal(GateVerdict.Failed,
            ArithmeticCorpusEvaluation.Verdicts(ArithmeticCorpusEvaluation.Load(CorpusPath), Silent).Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
    }
}
