using System.Text.Json;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>V1: damage is found in the raw answer, every attempt counts, and the decision table of contract 3.1 holds.</summary>
public sealed class NotesProbeTests : IDisposable
{
    private const string Block = "<!-- atelier-notes\n{\"assumptions\": [], \"open_points\": [], \"quotes\": []}\n-->";

    private readonly string _dir = Directory.CreateTempSubdirectory("notes-probe-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static WriterNotesSplit SimpleSplit(string raw)
    {
        var lines = raw.Split('\n').ToList();
        var start = lines.FindLastIndex(l => l.StartsWith(WriterNotesFormat.StartMarker, StringComparison.Ordinal));
        if (start < 0)
            return new WriterNotesSplit(raw, WriterNotes.Empty, WriterNotesStatus.Absent);
        var block = string.Join('\n', lines.Skip(start));
        var status = block.Contains("{\"assumptions\"", StringComparison.Ordinal) && block.Contains("-->", StringComparison.Ordinal)
            ? WriterNotesStatus.Ok : WriterNotesStatus.Unreadable;
        return new WriterNotesSplit(string.Join('\n', lines.Take(start)).TrimEnd(), WriterNotes.Empty, status);
    }

    private static ProbeOrder Order(string id, string size) =>
        new(id, size, "de", "Schreib einen Brief.", size == "short" ? 500 : 6000, [new ProbeFile("notizen.txt", "text", "Notizen " + id)]);

    private static ProbeLine Line(string id, string size, string status, params string[] damage) =>
        new(id, size, status, damage, 500, 500, "Ende.", 1_000, 900, "stop", "claude-opus-latest", null, status is "TimedOut" or "Failed" ? null : "raw", 1_800_000, "h-" + id, DateTimeOffset.UnixEpoch);

    private static IReadOnlyList<ProbeLine> Lines(int shortOk, int shortOther, int longOk, int longOther, string other = "Absent") =>
        Enumerable.Range(0, shortOk).Select(i => Line($"s{i}", "short", "Ok"))
            .Concat(Enumerable.Range(0, shortOther).Select(i => Line($"so{i}", "short", other)))
            .Concat(Enumerable.Range(0, longOk).Select(i => Line($"l{i}", "long", "Ok")))
            .Concat(Enumerable.Range(0, longOther).Select(i => Line($"lo{i}", "long", other)))
            .ToList();

    [Fact]
    public void A_clean_answer_has_no_damage()
    {
        var reading = NotesProbe.Read("Liebe Eltern,\nder Ausflug ist am Dienstag.\n\n" + Block, "stop", SimpleSplit);

        Assert.Equal("Ok", reading.Status);
        Assert.Empty(reading.Damage);
        Assert.Equal("der Ausflug ist am Dienstag.", reading.LastLine);
        Assert.Equal(7, reading.Words);
    }

    [Theory]
    [InlineData("Text.\n\n" + Block + "\nNoch ein Satz nach dem Block.", NotesProbe.BlockInsideText)]
    [InlineData("Text.\n" + Block + "\nMehr Text.\n" + Block, NotesProbe.BlockInsideText)]
    [InlineData("Text mit Rest --> im Satz.\n" + Block, NotesProbe.Residue)]
    [InlineData("Text.\n {\"assumptions\": []}\n" + Block, NotesProbe.Residue)]
    public void Damage_is_found_in_the_raw_answer(string raw, string damage) =>
        Assert.Contains(damage, NotesProbe.Read(raw, "stop", SimpleSplit).Damage);

    [Fact]
    public void An_answer_cut_off_at_the_token_limit_is_damaged()
    {
        Assert.Contains(NotesProbe.Truncated, NotesProbe.Read("Text ohne Ende und", "length", SimpleSplit).Damage);
        Assert.Equal("Absent", NotesProbe.Read("Text ohne Block.", "stop", SimpleSplit).Status);
    }

    [Fact]
    public void One_damage_is_outcome_c_even_after_one_attempt()
    {
        var (outcome, _) = NotesProbeEvaluation.Decide([Line("s0", "short", "Ok", NotesProbe.Residue)], []);

        Assert.Equal(ProbeOutcome.C, outcome);
    }

    [Fact]
    public void Stage_a_alone_decides_nothing()
    {
        var (outcome, detail) = NotesProbeEvaluation.Decide(Lines(4, 0, 2, 0), []);
        var (_, afterError) = NotesProbeEvaluation.Decide(Lines(4, 0, 1, 1), []);

        Assert.Equal(ProbeOutcome.NotEvaluable, outcome);
        Assert.Contains("stage b (--short 10 --long 5)", detail);
        Assert.Contains("stage c (--short 10 --long 10)", afterError);
    }

    [Fact]
    public void An_error_free_stage_b_is_outcome_a_and_one_absent_text_asks_for_stage_c()
    {
        Assert.Equal(ProbeOutcome.A, NotesProbeEvaluation.Decide(Lines(10, 0, 5, 0), []).Outcome);

        var (outcome, detail) = NotesProbeEvaluation.Decide(Lines(10, 0, 4, 1), []);
        Assert.Equal(ProbeOutcome.NotEvaluable, outcome);
        Assert.Contains("--long 10", detail);
    }

    [Theory]
    [InlineData(9, 1, 8, 2, ProbeOutcome.A)]
    [InlineData(10, 0, 6, 4, ProbeOutcome.B)]
    [InlineData(8, 2, 10, 0, ProbeOutcome.B)]
    [InlineData(7, 3, 10, 0, ProbeOutcome.C)]
    public void Stage_c_follows_the_decision_table(int shortOk, int shortOther, int longOk, int longOther, ProbeOutcome expected) =>
        Assert.Equal(expected, NotesProbeEvaluation.Decide(Lines(shortOk, shortOther, longOk, longOther), []).Outcome);

    [Fact]
    public void Failed_and_timed_out_attempts_count_as_not_ok()
    {
        Assert.Equal(ProbeOutcome.C, NotesProbeEvaluation.Decide(Lines(7, 3, 10, 0, "TimedOut"), []).Outcome);
        Assert.Equal(3, NotesProbeEvaluation.Figures("short", Lines(7, 3, 10, 0, "Failed")).NoAnswer);
    }

    [Fact]
    public void Results_for_another_input_make_the_file_not_evaluable()
    {
        var lines = Lines(10, 0, 5, 0);
        var hashes = lines.ToDictionary(l => l.Id, l => l.InputHash);
        hashes["s0"] = "new";

        var problems = NotesProbeEvaluation.Problems(lines, hashes);

        Assert.Single(problems);
        Assert.Equal(ProbeOutcome.NotEvaluable, NotesProbeEvaluation.Decide(lines, problems).Outcome);
    }

    [Fact]
    public void Rereading_uses_the_current_split_of_the_stored_raw_answer()
    {
        var stored = Line("s0", "short", "Ok") with { Raw = "Text.\n" + Block + "\nNachsatz." };

        var reread = NotesProbeEvaluation.Reread([stored], SimpleSplit).Single();

        Assert.Contains(NotesProbe.BlockInsideText, reread.Damage);
    }

    private sealed class ScriptedClient(Func<int, LlmResponse> answer) : ILlmClient
    {
        public int Calls { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => Task.FromResult(answer(++Calls));
    }

    private sealed class Guard(Func<int, string?> blocker) : ICodexGuard
    {
        private int _asked;

        public string? Blocker() => blocker(++_asked);
    }

    private static LlmRequest FakeRequest(ProbeOrder order) => new() { Model = "claude-opus-latest", SystemPrompt = "s", UserPrompt = order.Request + order.Id };

    private static LlmResponse Answer(string text) => new() { Text = text, FinishReason = "stop", TokenUsage = new LlmTokenUsage { InputTokens = 10, OutputTokens = 20 } };

    private async Task<(int Exit, ScriptedClient Client, IReadOnlyList<ProbeLine> Lines)> Probe(int shortCount, int longCount, int maxCalls, Func<int, string?>? blocker = null, Func<int, LlmResponse>? answer = null)
    {
        var inputs = Path.Combine(_dir, "orders.jsonl");
        var output = Path.Combine(_dir, "probe.jsonl");
        File.WriteAllLines(inputs, new[] { Order("k1", "short"), Order("k2", "short"), Order("k3", "short"), Order("l1", "long") }.Select(o => JsonSerializer.Serialize(o)));
        var client = new ScriptedClient(answer ?? (_ => Answer("Text.\n" + Block)));
        var runner = new NotesProbeRunner(new ProbeSettings(inputs, output, shortCount, longCount, maxCalls, TimeSpan.FromMinutes(1)),
            client, FakeRequest, new Guard(blocker ?? (_ => null)), SimpleSplit, new StringWriter());
        var exit = await runner.RunAsync(CancellationToken.None);
        return (exit, client, ResultFile.Load<ProbeLine>(output));
    }

    [Fact]
    public async Task Stages_are_cumulative_and_short_orders_take_turns()
    {
        var (exit, _, lines) = await Probe(4, 2, 20);
        Assert.Equal(0, exit);
        Assert.Equal(["k1", "k2", "k3", "k1", "l1", "l1"], lines.Select(l => l.Id));

        var (_, client, after) = await Probe(5, 2, 20);
        Assert.Equal(1, client.Calls);
        Assert.Equal("k2", after[^1].Id);
    }

    [Fact]
    public async Task The_guard_stops_before_a_call_and_the_file_cap_is_twenty()
    {
        var (blocked, client, lines) = await Probe(4, 0, 20, blocker: n => n == 2 ? "a Codex process runs" : null);
        Assert.Equal(FactCheckRunner.Blocked, blocked);
        Assert.Equal(1, client.Calls);
        Assert.Single(lines);

        var (capped, _, all) = await Probe(20, 20, 20);
        Assert.Equal(FactCheckRunner.Incomplete, capped);
        Assert.Equal(NotesProbeRunner.Ceiling, all.Count);
    }

    [Fact]
    public async Task A_failed_call_is_stored_as_an_attempt()
    {
        var (_, _, lines) = await Probe(1, 0, 1, answer: _ => throw new HttpRequestException("busy", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var line = Assert.Single(lines);
        Assert.Equal(NotesProbe.Failed, line.Status);
        Assert.Equal("http_503", line.ErrorCode);
        Assert.Null(line.Raw);
    }

    [Fact]
    public void The_background_document_is_rendered_by_the_real_renderer_with_ids_from_s1()
    {
        var bundle = NotesProbe.Bundle(Order("k1", "short") with { Files = [new ProbeFile("a.txt", "text", "Erste Datei"), new ProbeFile("b.md", "markdown", "Zweite Datei")] });

        Assert.Equal(["S1", "S2"], bundle.Sources.Select(s => s.Id));
        Assert.Contains("<source id=\"S2\" file=\"b.md\" type=\"markdown\"", bundle.ContextDocument);
        Assert.Contains("Zweite Datei", bundle.ContextDocument);
    }

    [Fact]
    public void The_probe_orders_follow_the_contract_sizes()
    {
        var orders = ResultFile.Load<ProbeOrder>(Path.Combine(FactCheckCorpusTests.CorpusDirectory(), FactCheckCorpusTests.ProbeOrders));

        var shortOrders = orders.Where(o => o.Size == "short").ToList();
        var longOrder = Assert.Single(orders, o => o.Size == "long");
        Assert.Equal(5, shortOrders.Count);
        Assert.All(shortOrders, o => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(Assert.Single(o.Files).Text), 2_000, 6_000));
        Assert.All(shortOrders, o => Assert.Equal(500, o.TargetWords));
        Assert.InRange(Assert.Single(longOrder.Files).Text.Length, 27_000, 33_000);
        Assert.Equal(6_000, longOrder.TargetWords);
        Assert.Equal(orders.Count, orders.Select(o => o.Id).Distinct().Count());
    }
}
