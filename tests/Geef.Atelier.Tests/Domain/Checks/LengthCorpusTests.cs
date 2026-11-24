using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Checks;

/// <summary>
/// Measures the length rule of <see cref="RequestConstraintExtractor"/> on synthetic requests whose truth was written
/// by a separate generator before the extractor saw them. The main corpus is split 70/30 by a hash of the request into
/// a development set (corrections were derived from it alone) and a held-out set; a second corpus written in a hasty
/// typing style is a further test set. A false block is a blocking length rule that does not fit the truth.
/// </summary>
public sealed class LengthCorpusTests(ITestOutputHelper output)
{
    public const string MainCorpus = "length-corpus.jsonl";
    public const string SecondCorpus = "length-corpus-second.jsonl";
    private const double MaxFalseBlockRate = 0.01;
    private const double RecallWarning = 0.60;
    private const double NumberTolerance = 0.25;

    public sealed record Truth(
        [property: JsonPropertyName("unit")] string Unit,
        [property: JsonPropertyName("min")] int? Min,
        [property: JsonPropertyName("max")] int? Max);

    public sealed record Item(
        [property: JsonPropertyName("request")] string Request,
        [property: JsonPropertyName("length")] Truth? Length,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("lang")] string Lang);

    public sealed record Miss(Item Item, LengthConstraint Rule);

    public sealed record Measurement(int Count, IReadOnlyList<Miss> FalseBlocks, int ClearWithTruth, int ClearHits, int ClearDirectionErrors)
    {
        public double FalseBlockRate => Count == 0 ? 0 : (double)FalseBlocks.Count / Count;

        public double ClearRecall => ClearWithTruth == 0 ? 0 : (double)ClearHits / ClearWithTruth;
    }

    public static IReadOnlyList<Item> Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Domain", "Checks", "Corpus", fileName);
        return File.ReadLines(path)
            .Where(l => l.TrimStart().StartsWith('{'))
            .Select(l => JsonSerializer.Deserialize<Item>(l)!)
            .ToList();
    }

    public static bool InDevelopmentSet(string request) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(request))[0] % 10 < 7;

    public static bool Fits(LengthConstraint rule, Truth truth)
    {
        var unit = truth.Unit == "chars" ? LengthUnit.Characters : LengthUnit.Words;
        if (rule.Unit != unit || rule.Min.HasValue != truth.Min.HasValue || rule.Max.HasValue != truth.Max.HasValue)
            return false;
        return Close(rule.Min, truth.Min) && Close(rule.Max, truth.Max);
    }

    public static bool SameDirection(LengthConstraint rule, Truth truth) =>
        rule.Min.HasValue == truth.Min.HasValue && rule.Max.HasValue == truth.Max.HasValue;

    public static Measurement Measure(IEnumerable<Item> items)
    {
        var list = items.ToList();
        var falseBlocks = new List<Miss>();
        int clearWithTruth = 0, clearHits = 0, directionErrors = 0;
        foreach (var item in list)
        {
            var rule = RequestConstraintExtractor.Extract(item.Request).Length;
            if (rule is not null && (item.Length is null || !Fits(rule, item.Length)))
                falseBlocks.Add(new Miss(item, rule));

            if (item.Category != "clear_single" || item.Length is null)
                continue;
            clearWithTruth++;
            if (rule is null)
                continue;
            if (Fits(rule, item.Length))
                clearHits++;
            if (!SameDirection(rule, item.Length))
                directionErrors++;
        }
        return new Measurement(list.Count, falseBlocks, clearWithTruth, clearHits, directionErrors);
    }

    [Fact]
    public void The_corpus_is_large_and_mixed_enough()
    {
        var main = Load(MainCorpus);
        var second = Load(SecondCorpus);

        Assert.True(main.Count >= 400, $"main corpus has {main.Count} requests");
        Assert.True(second.Count >= 150, $"second corpus has {second.Count} requests");
        Assert.True(Share(main, i => i.Lang == "de") >= 0.40, "German share");
        Assert.True(Share(main, i => i.Lang == "en") >= 0.40, "English share");
        Assert.Equal(main.Count, main.Select(i => i.Request).Distinct(StringComparer.Ordinal).Count());

        var minimum = new Dictionary<string, double>
        {
            ["clear_single"] = 0.30, ["multiple_items"] = 0.10, ["foreign_object"] = 0.08, ["revision_context"] = 0.10,
            ["quoted_or_meta"] = 0.06, ["negations_conjunctions"] = 0.10, ["other_units"] = 0.06, ["no_length"] = 0.10,
            ["adversarial"] = 0.10,
        };
        foreach (var (category, share) in minimum)
            Assert.True(Share(main, i => i.Category == category) >= share - 0.005, $"{category}: {Share(main, i => i.Category == category):P1}");

        var development = main.Count(i => InDevelopmentSet(i.Request));
        Assert.InRange((double)development / main.Count, 0.62, 0.78);
    }

    [Fact]
    public void Development_set_meets_the_targets() =>
        AssertTargets("development set", Load(MainCorpus).Where(i => InDevelopmentSet(i.Request)));

    [Fact]
    public void Held_out_set_meets_the_targets() =>
        AssertTargets("held-out set", Load(MainCorpus).Where(i => !InDevelopmentSet(i.Request)));

    [Fact]
    public void Second_corpus_meets_the_targets() =>
        AssertTargets("second corpus", Load(SecondCorpus));

    private void AssertTargets(string name, IEnumerable<Item> items)
    {
        var m = Measure(items);
        output.WriteLine($"{name}: {m.Count} requests, false blocks {m.FalseBlocks.Count} ({m.FalseBlockRate:P2}), clear recall {m.ClearHits}/{m.ClearWithTruth} ({m.ClearRecall:P1}), direction errors {m.ClearDirectionErrors}");
        foreach (var miss in m.FalseBlocks)
            output.WriteLine($"  false block [{miss.Item.Category}] {miss.Rule.Kind} {miss.Rule.Min}-{miss.Rule.Max} {miss.Rule.Unit}: {miss.Item.Request}");
        if (m.ClearRecall < RecallWarning)
            output.WriteLine($"  warning: clear recall below {RecallWarning:P0}");

        Assert.True(m.FalseBlockRate <= MaxFalseBlockRate,
            $"{name}: {m.FalseBlocks.Count} false blocks of {m.Count}: " + string.Join(" | ", m.FalseBlocks.Select(f => f.Item.Request)));
        Assert.Equal(0, m.ClearDirectionErrors);
    }

    private static bool Close(int? actual, int? expected) =>
        actual is null && expected is null
        || actual is { } a && expected is { } e && Math.Abs(a - e) <= e * NumberTolerance;

    private static double Share(IReadOnlyList<Item> items, Func<Item, bool> predicate) =>
        (double)items.Count(predicate) / items.Count;
}
