using System.Net;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.IntakeEval;
using CorpusProfile = Geef.Atelier.Tools.IntakeEval.CorpusProfile;
using Geef.Atelier.Tools.ResearchEval;
using static Geef.Atelier.Tests.ResearchEvalTool.ResearchEvalTestData;

namespace Geef.Atelier.Tests.ResearchEvalTool;

/// <summary>Order sets, runner, transport limits and file rules of <c>tools/ResearchEval</c> (contract stage 4, 3.3 and 7.5, F5a).</summary>
public sealed class ResearchRunnerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_stored_gate_output_gives_the_fixed_sets_of_the_contract()
    {
        var directory = Geef.Atelier.Tests.Intake.IntakeCorpusTests.CorpusDirectory();
        var corpus = new[] { "intake-corpus.jsonl", "intake-corpus-second.jsonl" }.SelectMany(n => Corpus.Load(Path.Combine(directory, n))).ToList();
        var (orders, problems) = ResearchCorpus.Load(JsonLines.Load<StoredGateLine>(Path.Combine(directory, "results-20261125.jsonl")), corpus);

        Assert.Empty(problems);
        Assert.Equal(
            ["main-004", "main-014", "main-016", "main-025", "main-029", "main-032", "main-033", "main-036", "main-038", "main-040", "main-041", "main-059"],
            ResearchCorpus.Set(orders, ResearchCorpus.DevSet).Select(o => o.Id));
        Assert.Equal(
            ["main-044", "main-046", "main-079", "main-086", "main-107", "main-116", "second-004", "second-026", "second-038"],
            ResearchCorpus.Set(orders, ResearchCorpus.HoldoutSet).Select(o => o.Id));
        var reserved = ResearchCorpus.Reserved(orders);
        Assert.Equal(15, reserved.Count);
        Assert.All(reserved, o => Assert.Null(ResearchCorpus.SetOf(orders, o.Id)));
        Assert.Equal(75, orders.Sum(o => o.Topics.Count));
        Assert.Contains(orders, o => o.ProfileText is not null);
    }

    [Fact]
    public void Another_number_of_orders_is_a_problem_and_no_set_may_be_measured()
    {
        var gate = new[] { new StoredGateLine("a", EvalBasics.Dev, "Ok", ["t"]), new StoredGateLine("a", EvalBasics.Dev, "Ok", ["t"]) };
        var (_, problems) = ResearchCorpus.Load(gate, [new CorpusEntry("a", "main", "x", "de", "r", null, null, null)]);

        Assert.Contains(problems, p => p.Contains("occurs 2 times", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("the contract expects 36", StringComparison.Ordinal));
    }

    [Fact]
    public void The_profile_text_holds_every_field()
    {
        var text = ResearchCorpus.ProfileText(new CorpusProfile("Ida Brenner", "Leiterin", "Verein", "formal", "de", "Gruß\nIda", "keine Drohungen", "frei"));

        Assert.Equal("Ida Brenner\nLeiterin\nVerein\nformal\nde\nGruß\nIda\nkeine Drohungen\nfrei", text);
        Assert.Null(ResearchCorpus.ProfileText(new CorpusProfile(null, " ", null, null, null, null, null, null)));
    }

    [Fact]
    public async Task Each_order_of_the_set_gets_one_line_with_fingerprint_raw_texts_and_entries()
    {
        var orders = SyntheticOrders();
        var outPath = _temp.File("run.jsonl");
        var transport = Transport();

        var exit = await Runner(outPath, ResearchCorpus.DevSet, orders, transport).RunAsync(CancellationToken.None);

        var lines = JsonLines.Load<ResearchRunLine>(outPath);
        Assert.Equal(0, exit);
        Assert.Equal(ResearchCorpus.Set(orders, ResearchCorpus.DevSet).Select(o => o.Id), lines.Select(l => l.Id));
        Assert.All(lines, l =>
        {
            Assert.Equal(Kit.InputHash(orders.Single(o => o.Id == l.Id), Settings), l.InputHash);
            Assert.Equal("Found", l.Status);
            Assert.Equal(2, l.TavilyRequests);
            Assert.Equal(1, l.ClaudeCalls);
            Assert.Equal(["P1", "P2"], l.Pages.Select(p => p.Id));
            Assert.All(l.Pages, p => Assert.Equal(JsonLines.Sha256(p.RawText), p.RawSha256));
            Assert.Equal(2, l.Entries.Count);
            Assert.Equal(1, l.Dropped["NotInPage"]);
            Assert.Equal(1, l.Dropped["UnknownPage"]);
            Assert.Equal("{\"passages\":[]}", l.Condense!.Raw);
        });
        Assert.Equal(24, transport.Searches.Used);
        Assert.Equal(12, transport.Claude.Used);
    }

    [Fact]
    public async Task A_second_run_never_repeats_an_order_whatever_its_status()
    {
        var orders = SyntheticOrders();
        var outPath = _temp.File("run.jsonl");
        await Runner(outPath, ResearchCorpus.HoldoutSet, orders, Transport(), condenseError: t => t[0].Contains("h-02", StringComparison.Ordinal) ? "condense_failed" : null)
            .RunAsync(CancellationToken.None);

        var again = Transport();
        var exit = await Runner(outPath, ResearchCorpus.HoldoutSet, orders, again).RunAsync(CancellationToken.None);

        var lines = JsonLines.Load<ResearchRunLine>(outPath);
        Assert.Equal(0, exit);
        Assert.Equal(9, lines.Count);
        Assert.Equal(0, again.Searches.Used + again.Claude.Used);
        var failed = Assert.Single(lines, l => l.Status == "Failed");
        Assert.Equal("h-02", failed.Id);
        Assert.Equal(RunStatus.CondenseFailed, failed.ErrorCode);
        Assert.Equal(1, failed.ClaudeCalls);
        Assert.True(RunStatus.HasLatency(failed));
    }

    [Fact]
    public async Task Lines_for_another_input_make_the_run_refuse_before_any_call()
    {
        var orders = SyntheticOrders();
        var outPath = _temp.File("run.jsonl");
        await Runner(outPath, ResearchCorpus.DevSet, orders, Transport()).RunAsync(CancellationToken.None);
        var changed = Kit with { SystemPrompt = "another prompt" };
        var transport = Transport();

        var exit = await Runner(outPath, ResearchCorpus.DevSet, orders, transport, kit: changed).RunAsync(CancellationToken.None);

        Assert.Equal(ResearchRunner.Refused, exit);
        Assert.Equal(0, transport.Searches.Used + transport.Claude.Used);
    }

    [Fact]
    public async Task The_guard_stops_before_any_call()
    {
        var outPath = _temp.File("run.jsonl");
        var transport = Transport();

        var exit = await Runner(outPath, ResearchCorpus.DevSet, SyntheticOrders(), transport, new ClosedGuard()).RunAsync(CancellationToken.None);

        Assert.Equal(ResearchRunner.Blocked, exit);
        Assert.Empty(JsonLines.Load<ResearchRunLine>(outPath));
        Assert.Equal(0, transport.Searches.Used + transport.Claude.Used);
    }

    [Fact]
    public async Task An_order_starts_only_when_both_budgets_cover_it_completely()
    {
        var outPath = _temp.File("run.jsonl");
        var transport = Transport(searches: 3);

        var exit = await Runner(outPath, ResearchCorpus.DevSet, SyntheticOrders(), transport).RunAsync(CancellationToken.None);

        Assert.Equal(ResearchRunner.Incomplete, exit);
        Assert.Single(JsonLines.Load<ResearchRunLine>(outPath));
        Assert.Equal(2, transport.Searches.Used);

        var calls = Transport(calls: 1, callsUsed: 0);
        var second = _temp.File("run.jsonl");
        Assert.Equal(ResearchRunner.Incomplete, await Runner(second, ResearchCorpus.DevSet, SyntheticOrders(), calls).RunAsync(CancellationToken.None));
        Assert.Single(JsonLines.Load<ResearchRunLine>(second));
    }

    [Fact]
    public void The_file_ceilings_count_what_earlier_runs_used()
    {
        var used = ResearchRunner.Used([Line("a", 4, 1), Line("b", 3, 0)]);
        var transport = Transport(searches: 84, calls: 33, searchesUsed: used.Searches, callsUsed: used.Calls);

        Assert.Equal((7, 1), used);
        Assert.Equal(ResearchRunner.SearchCeiling - 7, transport.Searches.Remaining);
        Assert.Equal(ResearchRunner.ClaudeCeiling - 1, transport.Claude.Remaining);
    }

    [Fact]
    public async Task All_topics_withheld_means_no_search_and_no_model_call()
    {
        var orders = SyntheticOrders(id => id == "h-01" ? ["withheld topic one", "withheld topic two"] : [$"topic {id}"]);
        var outPath = _temp.File("run.jsonl");
        var transport = Transport();

        await Runner(outPath, ResearchCorpus.HoldoutSet, orders, transport).RunAsync(CancellationToken.None);

        var line = JsonLines.Load<ResearchRunLine>(outPath).Single(l => l.Id == "h-01");
        Assert.Equal("AllWithheld", line.Status);
        Assert.Equal(2, line.Withheld);
        Assert.Empty(line.Sent);
        Assert.Equal(0, line.TavilyRequests + line.ClaudeCalls);
        Assert.Null(line.Condense);
        Assert.False(RunStatus.HasLatency(line));
        Assert.Equal(8, transport.Searches.Used);
    }

    [Fact]
    public async Task Failed_searches_end_without_model_call_and_a_picker_timeout_stays_a_timeout()
    {
        var outPath = _temp.File("run.jsonl");
        await Runner(outPath, ResearchCorpus.HoldoutSet, SyntheticOrders(), Transport(),
            searchError: q => q.Contains("h-03", StringComparison.Ordinal) ? "timeout" : null,
            condenseError: t => t[0].Contains("h-04", StringComparison.Ordinal) ? "timeout" : null).RunAsync(CancellationToken.None);

        var lines = JsonLines.Load<ResearchRunLine>(outPath);
        var searchFailed = lines.Single(l => l.Id == "h-03");
        Assert.Equal((RunStatus.SearchFailed, 0), (searchFailed.ErrorCode, searchFailed.ClaudeCalls));
        Assert.False(RunStatus.HasLatency(searchFailed));
        var timedOut = lines.Single(l => l.Id == "h-04");
        Assert.Equal((RunStatus.Timeout, 1), (timedOut.ErrorCode, timedOut.ClaudeCalls));
        Assert.True(RunStatus.HasLatency(timedOut));
        Assert.False(RunStatus.ValidAnswer(timedOut));
    }

    [Fact]
    public async Task A_model_call_with_failover_on_is_refused_and_stops_the_run()
    {
        var outPath = _temp.File("run.jsonl");
        var transport = Transport();

        var exit = await Runner(outPath, ResearchCorpus.DevSet, SyntheticOrders(), transport, failoverOff: false).RunAsync(CancellationToken.None);

        Assert.Equal(ResearchRunner.Refused, exit);
        Assert.Contains("failover", transport.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, transport.Claude.Used);
        Assert.Single(JsonLines.Load<ResearchRunLine>(outPath));
    }

    [Fact]
    public void The_transport_lets_out_only_sent_topics_within_the_cap()
    {
        var transport = Transport(searches: 1);
        transport.BeginOrder(["allowed topic"]);

        Assert.False(transport.TakeSearch(TavilyProbe.Body("Request of d-01 with the whole wording", null)));
        Assert.Contains("other than a topic", transport.Refusal, StringComparison.Ordinal);

        var fresh = Transport(searches: 1);
        fresh.BeginOrder(["allowed topic"]);
        Assert.True(fresh.TakeSearch(TavilyProbe.Body("allowed topic", null)));
        Assert.False(fresh.TakeSearch(TavilyProbe.Body("allowed topic", null)));
        Assert.Contains("cap", fresh.Refusal, StringComparison.Ordinal);
        Assert.False(Transport().TakeSearch("not json"));
    }

    [Fact]
    public async Task A_refused_search_never_reaches_the_network()
    {
        var transport = Transport();
        transport.BeginOrder(["allowed topic"]);
        var inner = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var client = new SharedHandlerClientFactory(new SearchGuardHandler(transport) { InnerHandler = inner }, new Uri("https://search.example/"), TimeSpan.FromSeconds(5))
            .CreateClient("tavily");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("search", new StringContent(TavilyProbe.Body("other topic", null))));
        Assert.Empty(inner.Bodies);

        var ok = Transport();
        ok.BeginOrder(["allowed topic"]);
        var second = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var allowed = new SharedHandlerClientFactory(new SearchGuardHandler(ok) { InnerHandler = second }, new Uri("https://search.example/"), TimeSpan.FromSeconds(5))
            .CreateClient("tavily");
        using var response = await allowed.PostAsync("search", new StringContent(TavilyProbe.Body("allowed topic", null)));
        Assert.Single(second.Bodies);
        Assert.Equal(1, ok.TavilyRequests);
    }

    [Fact]
    public async Task Only_the_Claude_route_is_resolved_and_its_calls_need_failover_off()
    {
        var transport = Transport();
        var inner = new FixedResolver();
        var resolver = new GuardedResolver(inner, transport);

        Assert.Throws<InvalidOperationException>(() => resolver.ForProfile(ModelMatrix.CodexCli, "gpt-sol-latest", null));
        var (client, _, _) = resolver.ForProfile(ModelMatrix.ClaudeCli, "claude-sonnet-latest", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(new LlmRequest { Model = "m", SystemPrompt = "s", UserPrompt = "u" }, CancellationToken.None));
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public void Written_lines_never_hold_the_key_and_results_stay_out_of_git()
    {
        var json = JsonLines.Serialize(new { detail = $"invalid key {Secret}", quoted = $"\"{Secret}\"" }, [Secret, null, "short"]);

        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
        Assert.Contains(JsonLines.Redacted, json, StringComparison.Ordinal);

        var repo = Path.GetDirectoryName(_temp.File("repo"))!;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(Path.Combine(repo, "out"));
        Assert.NotNull(OutputLocation.Problem(Path.Combine(repo, "out", "run.jsonl")));
        Assert.Null(OutputLocation.Problem(_temp.File("run.jsonl")));
    }

    private static ResearchRunLine Line(string id, int searches, int calls) =>
        new(id, ResearchCorpus.DevSet, "Found", null, [], [], 0, 0, searches, calls, [], [], null, [], new Dictionary<string, int>(), 0, 0,
            DateTimeOffset.UnixEpoch, Settings, "h", DateTimeOffset.UnixEpoch);

    private sealed class FixedResolver : ILlmClientResolver, ILlmClient
    {
        public int Calls { get; private set; }

        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) => throw new NotSupportedException();

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) => (this, model, 100);

        public bool SupportsAgenticTools(string providerName) => false;

        public bool SupportsStructuredOutputs(string providerName) => true;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LlmResponse { Text = "{}", TokenUsage = new LlmTokenUsage { InputTokens = 1, OutputTokens = 1 }, FinishReason = "stop" });
        }
    }
}

/// <summary>Answers every request with a fixed status and body and keeps the request bodies and headers.</summary>
internal sealed class RecordingHandler(HttpStatusCode status, string body, Func<HttpRequestMessage, string, (HttpStatusCode, string)>? answer = null) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];

    public List<string?> Authorizations { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(text);
        Authorizations.Add(request.Headers.Authorization?.ToString());
        var (code, reply) = answer?.Invoke(request, text) ?? (status, body);
        return new HttpResponseMessage(code) { Content = new StringContent(reply) };
    }
}
