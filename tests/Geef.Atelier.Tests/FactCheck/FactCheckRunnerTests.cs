using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Tools.FactCheckEval;
using static Geef.Atelier.Tests.FactCheck.FactCheckTestData;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary><c>FactCheckEval run</c>: one call at a time, the guard before every call, every attempt in the file, a fixed cap.</summary>
public sealed class FactCheckRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("factcheck-run-").FullName;

    private string CorpusPath => Path.Combine(_dir, "factcheck-corpus.jsonl");

    private string OutPath => Path.Combine(_dir, "results.jsonl");

    public FactCheckRunnerTests() =>
        File.WriteAllLines(CorpusPath, Bases().Select(b => JsonSerializer.Serialize(b)));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class ScriptedChecker(Func<int, FactCheckCall> answer) : IFactChecker
    {
        public List<string> Drafts { get; } = [];

        public Task<FactCheckCall> CheckAsync(FactMaterial material, string draft, ReviewerProfile profile, TimeSpan timeout, int iteration, CancellationToken ct)
        {
            Drafts.Add(draft);
            return Task.FromResult(answer(Drafts.Count));
        }
    }

    private sealed class CountingGuard(Func<int, string?> blocker) : ICodexGuard
    {
        public int Asked { get; private set; }

        public string? Blocker() => blocker(++Asked);
    }

    private static FactCheckCall Ok(int _) => new([], "{\"findings\":[]}", 1_000, "gpt-astra-latest", null);

    private async Task<int> Run(ScriptedChecker checker, ICodexGuard guard, int maxCalls = 52, string split = "all", StringWriter? log = null) =>
        await new FactCheckRunner(new FactCheckRunSettings(CorpusPath, OutPath, split, maxCalls, TimeSpan.FromSeconds(180), null),
            checker, SystemCrew.SourceFactCheckProfile, guard, Kit(), log ?? new StringWriter()).RunAsync(CancellationToken.None);

    private IReadOnlyList<FactCheckLine> Lines => ResultFile.Load<FactCheckLine>(OutPath);

    [Fact]
    public void The_status_codes_of_the_tool_are_the_error_codes_of_the_checker()
    {
        Assert.Equal(LlmFactChecker.TimeoutCode, CallStatus.TimeoutCode);
        Assert.Equal(new SortedSet<string>(StringComparer.Ordinal) { LlmFactChecker.NoJsonCode, LlmFactChecker.UnusableCode },
            new SortedSet<string>(CallStatus.InvalidCodes, StringComparer.Ordinal));
        Assert.Equal(CallStatus.TimedOut, CallStatus.Of(LlmFactChecker.TimeoutCode, hasFindings: false));
        Assert.Equal(CallStatus.Invalid, CallStatus.Of(LlmFactChecker.NoJsonCode, hasFindings: false));
        Assert.Equal(CallStatus.Invalid, CallStatus.Of(LlmFactChecker.UnusableCode, hasFindings: false));
    }

    [Fact]
    public async Task Every_text_of_the_split_gets_one_line_with_hash_status_and_time_limit()
    {
        var checker = new ScriptedChecker(Ok);

        var exit = await Run(checker, new CountingGuard(_ => null), split: EvalBasics.Dev);

        var dev = FactCheckCorpus.Items(Bases()).Where(i => i.Split == EvalBasics.Dev).ToList();
        Assert.Equal(0, exit);
        Assert.Equal(dev.Select(i => i.Id), Lines.Select(l => l.Id));
        Assert.All(Lines, l => Assert.Equal(CallStatus.Ok, l.Status));
        Assert.All(Lines, l => Assert.Equal(180_000, l.TimeoutMs));
        Assert.Equal(dev.Select(i => Kit().InputHash(i)), Lines.Select(l => l.InputHash));
    }

    [Fact]
    public async Task The_guard_is_asked_before_every_call_and_a_block_stops_the_run_without_a_call()
    {
        var checker = new ScriptedChecker(Ok);
        var guard = new CountingGuard(n => n == 3 ? "lock file /x exists" : null);
        var log = new StringWriter();

        var exit = await Run(checker, guard, log: log);

        Assert.Equal(FactCheckRunner.Blocked, exit);
        Assert.Equal(2, checker.Drafts.Count);
        Assert.Equal(3, guard.Asked);
        Assert.Equal(2, Lines.Count);
        Assert.Contains("lock file /x exists", log.ToString());
    }

    [Fact]
    public async Task Failed_attempts_stay_in_the_file_and_are_called_again_while_answers_are_skipped()
    {
        var first = new ScriptedChecker(n => n == 1 ? new FactCheckCall(null, null, 300, "gpt-astra-latest", "backend_unavailable") : n == 2 ? new FactCheckCall(null, "no", 4_000, "gpt-astra-latest", "no_json") : Ok(n));
        Assert.Equal(FactCheckRunner.Incomplete, await Run(first, new CountingGuard(_ => null)));
        var total = Lines.Count;

        var second = new ScriptedChecker(Ok);
        Assert.Equal(0, await Run(second, new CountingGuard(_ => null)));

        Assert.Single(second.Drafts);
        Assert.Equal(total + 1, Lines.Count);
        Assert.Equal([CallStatus.Failed, CallStatus.Invalid], Lines.Take(2).Select(l => l.Status));
        Assert.Equal(Lines[0].Id, Lines[^1].Id);
    }

    [Fact]
    public async Task The_cap_of_the_run_and_of_the_file_are_never_exceeded()
    {
        Assert.Equal(FactCheckRunner.Incomplete, await Run(new ScriptedChecker(n => new FactCheckCall(null, null, 10, "m", "http_503")), new CountingGuard(_ => null), maxCalls: 5));
        Assert.Equal(5, Lines.Count);

        for (var i = 0; i < 20; i++)
            await Run(new ScriptedChecker(n => new FactCheckCall(null, null, 10, "m", "http_503")), new CountingGuard(_ => null), maxCalls: 52);

        Assert.Equal(FactCheckRunner.Ceiling, Lines.Count);
    }

    [Fact]
    public async Task An_incomplete_corpus_is_refused_before_any_call()
    {
        File.WriteAllLines(CorpusPath, Bases().Skip(1).Select(b => JsonSerializer.Serialize(b)));
        var checker = new ScriptedChecker(Ok);

        Assert.Equal(2, await Run(checker, new CountingGuard(_ => null)));
        Assert.Empty(checker.Drafts);
    }

    [Fact]
    public async Task Results_for_another_input_are_called_again_with_a_warning()
    {
        await Run(new ScriptedChecker(Ok), new CountingGuard(_ => null), split: EvalBasics.Holdout);
        var changed = Lines.Select(l => l with { InputHash = "old" }).ToList();
        File.WriteAllLines(OutPath, changed.Select(l => JsonSerializer.Serialize(l)));
        var log = new StringWriter();
        var checker = new ScriptedChecker(Ok);

        await Run(checker, new CountingGuard(_ => null), split: EvalBasics.Holdout, log: log);

        Assert.Equal(changed.Count, checker.Drafts.Count);
        Assert.Contains("another input", log.ToString());
    }

    [Theory]
    [InlineData(null, true, CallStatus.Ok)]
    [InlineData("no_json", false, CallStatus.Invalid)]
    [InlineData("unusable_json", false, CallStatus.Invalid)]
    [InlineData("timeout", false, CallStatus.TimedOut)]
    [InlineData("backend_unavailable", false, CallStatus.Failed)]
    [InlineData("http_429", false, CallStatus.Failed)]
    public void Checker_error_codes_map_to_line_statuses(string? code, bool findings, string status) =>
        Assert.Equal(status, CallStatus.Of(code, findings));
}
