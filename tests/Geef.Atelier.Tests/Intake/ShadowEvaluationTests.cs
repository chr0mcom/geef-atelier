using Geef.Atelier.Tools.IntakeEval;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// Gate g and the shadow parts of gates a and f from a CSV export of the shadow table: failed preparations carry no latency,
/// missing question counts are no zero, skipped and pending rows are left out, and no gate passes without enough data.
/// </summary>
public sealed class ShadowEvaluationTests
{
    private const string Header = "OrderId,Status,Attempts,DurationMs,QuestionCount,ErrorCode,CreatedAt,CompletedAt,Questions";

    private static IReadOnlyList<ShadowRow> Read(params string[] rows) =>
        ShadowEvaluation.Read(new StringReader(string.Join('\n', [Header, .. rows]) + "\n"));

    private static string Row(int i, string status, int? ms, int? questions, string? error = null, int day = 0, string? texts = null) =>
        $"{Guid.NewGuid()},{status},1,{ms},{questions},{error},2026-12-{1 + day:00}T10:{i % 60:00}:00+00:00,2026-12-{1 + day:00}T10:{i % 60:00}:30+00:00,{texts}";

    private static GateResult Gate(IReadOnlyList<GateResult> verdicts, string gate, string scope = "shadow") =>
        Assert.Single(verdicts, v => v.Gate == gate && v.Scope == scope);

    [Fact]
    public void Failed_preparations_have_no_latency_missing_counts_are_no_zero_and_skipped_or_pending_rows_are_left_out()
    {
        var rows = Read(
            Row(1, "Done", 10_000, 2),
            Row(2, "Done", 20_000, 0),
            Row(3, "Failed", 0, null, "prepare_failed"),
            Row(4, "TimedOut", 40_000, null),
            Row(5, "SkippedCap", null, null),
            Row(6, "Pending", null, null));

        var f = ShadowEvaluation.Measure(rows);

        Assert.Equal((6, 4, 1, 1, 1, 1), (f.Rows, f.Finished, f.SkippedCap, f.Pending, f.PrepareFailed, f.TimedOut));
        Assert.Equal(3, f.Timed);
        Assert.Equal(20_000, f.MedianMs);
        Assert.Equal(2, f.WithQuestionCount);
        Assert.Equal(1.0, f.MeanQuestions);
        Assert.Equal(0.5, f.FailureRate);
    }

    [Fact]
    public void Quoted_fields_with_commas_quotes_and_line_breaks_are_read()
    {
        var rows = Read(Row(1, "Done", 10_000, 2, texts: "\"Wer kommt, und wann?\n | Sag \"\"Hallo\"\"?\""));

        var row = Assert.Single(rows);
        Assert.Equal(["Wer kommt, und wann?", "Sag \"Hallo\"?"], row.Questions);
    }

    [Fact]
    public void A_missing_column_is_an_error_not_a_silent_zero() =>
        Assert.Throws<FormatException>(() => ShadowEvaluation.Read(new StringReader("OrderId,Status\n1,Done\n")));

    [Fact]
    public void An_empty_export_passes_no_gate()
    {
        var verdicts = ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(Read()));

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
    }

    [Fact]
    public void Gate_g_needs_thirty_orders_or_a_week()
    {
        var few = Enumerable.Range(0, 29).Select(i => Row(i, "Done", 10_000, 0)).ToArray();
        Assert.Equal(GateVerdict.NotEvaluable, Gate(ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(Read(few))), "g").Verdict);

        var thirty = Enumerable.Range(0, 30).Select(i => Row(i, "Done", 10_000, 0)).ToArray();
        Assert.Equal(GateVerdict.Passed, Gate(ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(Read(thirty))), "g").Verdict);

        var week = new[] { Row(1, "Done", 10_000, 0, day: 0), Row(2, "Done", 10_000, 1, day: 7) };
        Assert.Equal(GateVerdict.Passed, Gate(ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(Read(week))), "g").Verdict);
    }

    [Fact]
    public void Too_many_questions_slow_calls_or_failures_fail_the_shadow_gates()
    {
        var rows = Enumerable.Range(0, 30).Select(i => Row(i, "Done", 30_000, 2))
            .Concat(Enumerable.Range(30, 3).Select(i => Row(i, "Failed", 5_000, null)))
            .ToArray();

        var verdicts = ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(Read(rows)));

        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "a").Verdict);
        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "f").Verdict);
        Assert.Equal(GateVerdict.Failed, Gate(verdicts, "g").Verdict);
    }

    [Fact]
    public void The_visual_check_is_always_left_to_a_person_and_gets_a_spread_sample()
    {
        var rows = Read(Enumerable.Range(0, 30).Select(i => Row(i, "Done", 10_000, 1, texts: $"Frage {i}?")).ToArray());

        var verdicts = ShadowEvaluation.Verdicts(ShadowEvaluation.Measure(rows));

        Assert.Equal(GateVerdict.NotEvaluable, Gate(verdicts, "g", "visual check").Verdict);
        var sample = ShadowEvaluation.Sample(rows, 10);
        Assert.Equal(10, sample.Count);
        Assert.Equal("Frage 0?", sample[0]);
        Assert.Equal("Frage 27?", sample[9]);
    }
}
