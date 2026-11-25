using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Research;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.IntakeEval;
using Geef.Atelier.Tools.ResearchEval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

const string Usage = """
    Usage: ResearchEval orders   --intake-results <results-20261125.jsonl> --corpus <intake-corpus.jsonl> --corpus <intake-corpus-second.jsonl>
           ResearchEval probe-tavily --intake-results … --corpus … --corpus … --out <probe.jsonl> --max-searches N --codex-lock <file> [...]
                                     [--timeout-seconds 20]
           ResearchEval run      --intake-results … --corpus … --corpus … --set dev|holdout --gateway <url> --out <run.jsonl>
                                 --max-searches N --max-claude N --codex-lock <file> [...] [--results-per-search 3] [--raw-chars-per-page 6000]
           ResearchEval judge    --run <run-holdout.jsonl> --gateway <url> --out <judge.jsonl> --max-claude N --codex-lock <file> [...]
                                 [--timeout-seconds 240]
           ResearchEval evaluate --intake-results … --corpus … --corpus … --holdout <run-holdout.jsonl> [--also <run-dev.jsonl> ...]
                                 --judgments <judge.jsonl> --verification <verify.json> [--results-per-search 3] [--raw-chars-per-page 6000]

    orders        Prints the order sets of VR (12 development orders, 9 held-out orders, 15 reserved for R3). No call.
    probe-tavily  VT: one Tavily search per topic (first topic the query gate sends for the first development orders), search_depth
                  basic, max_results 3, include_raw_content true, include_answer false; after a 401/403 with the key in the body one
                  request with Authorization: Bearer. At most 3 requests per file; prints the decision of contract stage 4, 3.2.
    run           VR: real ResearchQueryGate, TavilySearchClient, LlmDossierCondenser (Sonnet, failover off) and DossierBuilder for
                  every order of the set, one order after another; one line per order with raw texts, proposals, entries and the
                  input fingerprint. Each order is measured once per file; failed orders stay and count.
    judge         VR: one blind Opus call per held-out order with entries (topics and passages only): does each passage help.
    evaluate      VR: gate on the held-out set (K1 with the independent check, validity, yield, relevance, median duration),
                  development figures without verdict. Never passed without data.

    The caps are required: --max-searches (Tavily requests of this run; at most 84 per run file, 3 per probe file) and --max-claude
    (model calls of this run; at most 33 per run file, 9 per judgement file). The transport refuses every request beyond them, every
    search that carries anything but a sent topic and every model call with the gateway failover on. Codex is never called.
    Before every order the tool stops (exit 3) while a --codex-lock file exists or `pgrep -a codex` finds a Codex job.
    Result files must lie outside Git (raw page texts are third-party content), for example under $EVAL_RESEARCH_DIR.
    Keys only from the environment: TAVILY_API_KEY (Tavily) and RESEARCH_EVAL_API_KEY (gateway, if it needs one); never printed,
    removed from every written line.
    Exit codes: 0 done / passed / continue, 1 gate failed / halt, 2 wrong usage or refused, 3 stopped by the guard,
    4 incomplete, not evaluable or production code not implemented on this branch yet.
    """;

const string TavilyKeyVariable = "TAVILY_API_KEY";
const string GatewayKeyVariable = "RESEARCH_EVAL_API_KEY";

string[] commands = ["orders", "probe-tavily", "run", "judge", "evaluate"];
if (args.Length == 0 || !commands.Contains(args[0]))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

try
{
    return args[0] switch
    {
        "orders" => Orders(Parse("intake-results", "corpus")),
        "probe-tavily" => await ProbeAsync(Parse("intake-results", "corpus", "out", "max-searches", "codex-lock", "timeout-seconds"), stop.Token),
        "run" => await RunAsync(Parse("intake-results", "corpus", "set", "gateway", "out", "max-searches", "max-claude", "codex-lock", "results-per-search", "raw-chars-per-page"), stop.Token),
        "judge" => await JudgeAsync(Parse("run", "gateway", "out", "max-claude", "codex-lock", "timeout-seconds"), stop.Token),
        _ => Evaluate(Parse("intake-results", "corpus", "holdout", "also", "judgments", "verification", "results-per-search", "raw-chars-per-page")),
    };
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}
catch (NotImplementedException)
{
    Console.Error.WriteLine("A part of the production code this command needs is not implemented on this branch yet (merge order F1 → F2 → F5a, see tools/ResearchEval/README.md).");
    return 4;
}

CommandLine Parse(params string[] allowed) => CommandLine.Parse(args[1..], allowed);

static IReadOnlyList<ResearchOrder> LoadOrders(CommandLine line)
{
    var corpus = line.All("corpus");
    if (corpus.Count == 0)
        throw new UsageException("--corpus is required (both intake corpus files)");
    var (orders, problems) = ResearchCorpus.Load(JsonLines.Load<StoredGateLine>(line.Required("intake-results")), corpus.SelectMany(Corpus.Load).ToList());
    if (problems.Count > 0)
        throw new UsageException($"the order sets of VR cannot be formed: {string.Join("; ", problems)}");
    return orders;
}

static IReadOnlyList<string> Locks(CommandLine line)
{
    var locks = line.All("codex-lock");
    return locks.Count > 0 ? locks : throw new UsageException("--codex-lock is required (path of the lock file of the shared Codex subscription)");
}

static string Out(CommandLine line)
{
    var path = line.Required("out");
    return OutputLocation.Problem(path) is { } problem ? throw new UsageException(problem) : path;
}

static string TavilyKey() =>
    Environment.GetEnvironmentVariable(TavilyKeyVariable) is { Length: > 0 } key ? key : throw new UsageException($"{TavilyKeyVariable} is not set");

static string? GatewayKey() => Environment.GetEnvironmentVariable(GatewayKeyVariable) is { Length: > 0 } key ? key : null;

static Uri TavilyEndpoint() => new(new TavilyOptions().Endpoint.TrimEnd('/') + "/");

static ResearchOptions Settings(CommandLine line)
{
    var defaults = new ResearchOptions();
    return new ResearchOptions
    {
        ResultsPerSearch = line.Number("results-per-search", 1, 10, defaults.ResultsPerSearch),
        RawCharsPerPage = line.Number("raw-chars-per-page", 500, 50_000, defaults.RawCharsPerPage),
    };
}

static ModelCandidate Claude(ModelCandidate candidate) =>
    candidate.Provider == ModelMatrix.ClaudeCli
        ? candidate
        : throw new UsageException($"the model {candidate.Provider}/{candidate.Model} is not served by the Claude subscription; VR calls no Codex");

static int Orders(CommandLine line)
{
    var orders = LoadOrders(line);
    foreach (var set in new[] { ResearchCorpus.DevSet, ResearchCorpus.HoldoutSet })
    {
        var members = ResearchCorpus.Set(orders, set);
        Console.WriteLine($"{set} ({members.Count}): {string.Join(", ", members.Select(o => $"{o.Id} [{o.Topics.Count}]"))}");
    }
    Console.WriteLine($"reserved for R3 ({ResearchCorpus.Reserved(orders).Count}), never measured here.");
    return 0;
}

static async Task<int> ProbeAsync(CommandLine line, CancellationToken ct)
{
    var orders = LoadOrders(line);
    var outPath = Out(line);
    var maxSearches = line.Number("max-searches", 1, TavilyProbe.Ceiling);
    var guard = CodexGuard.ForHost(Locks(line));
    var key = TavilyKey();
    var topics = TavilyProbe.Topics(orders, ResearchKit.Real, new ResearchOptions().MaxSearches);
    using var http = new HttpClient { BaseAddress = TavilyEndpoint(), Timeout = TimeSpan.FromSeconds(line.Number("timeout-seconds", 5, 120, 20)) };
    return await new TavilyProbe(http, key, guard, TimeProvider.System, Console.Error).RunAsync(topics, outPath, maxSearches, ct);
}

static async Task<int> RunAsync(CommandLine line, CancellationToken ct)
{
    var orders = LoadOrders(line);
    var set = line.Required("set");
    if (set is not (ResearchCorpus.DevSet or ResearchCorpus.HoldoutSet))
        throw new UsageException("--set must be dev or holdout");
    var outPath = Out(line);
    var maxSearches = line.Number("max-searches", 1, ResearchRunner.SearchCeiling);
    var maxClaude = line.Number("max-claude", 1, ResearchRunner.ClaudeCeiling);
    var gateway = line.Required("gateway");
    var guard = CodexGuard.ForHost(Locks(line));
    var options = Settings(line);
    var key = TavilyKey();
    var gatewayKey = GatewayKey();
    var picker = Claude(ModelMatrix.Primary(ModelSlot.GroundingRefiner));

    var (searchesUsed, callsUsed) = ResearchRunner.Used(JsonLines.Load<ResearchRunLine>(outPath));
    var transport = new ResearchTransport(
        new CallBudget(maxSearches, ResearchRunner.SearchCeiling, searchesUsed),
        new CallBudget(maxClaude, ResearchRunner.ClaudeCeiling, callsUsed));
    using var handler = new SearchGuardHandler(transport) { InnerHandler = new SocketsHttpHandler() };
    var factory = new SharedHandlerClientFactory(handler, TavilyEndpoint(), TimeSpan.FromSeconds(120));
    var search = new TavilySearchClient(factory, Options.Create(new TavilyOptions { ApiKey = key }),
        Options.Create(options), TimeProvider.System);
    using var llmHttp = new HttpClient { Timeout = options.CondenseTimeout + TimeSpan.FromSeconds(60) };
    var resolver = new GuardedResolver(new GatewayResolver(llmHttp, gateway, gatewayKey), transport);
    var condenser = new LlmDossierCondenser(new FixedModelAssignment(picker), resolver, Options.Create(options),
        NullLogger<LlmDossierCondenser>.Instance, TimeProvider.System);
    Console.Error.WriteLine($"Passage picker {picker.Provider}/{picker.Model}, failover off; set {set}; {options.ResultsPerSearch} results per search, {options.RawCharsPerPage} raw characters per page.");

    var runner = new ResearchRunner(new ResearchRunSettings(outPath, set, options), orders, RealResearchParts.Search(search), RealResearchParts.Condense(condenser),
        transport, guard, ResearchKit.Real, TimeProvider.System, Console.Error, [key, gatewayKey]);
    return await runner.RunAsync(ct);
}

static async Task<int> JudgeAsync(CommandLine line, CancellationToken ct)
{
    var runPath = line.Required("run");
    if (!File.Exists(runPath))
        throw new UsageException($"{runPath} does not exist");
    var outPath = Out(line);
    var maxClaude = line.Number("max-claude", 1, RelevanceJudge.Ceiling);
    var gateway = line.Required("gateway");
    var guard = CodexGuard.ForHost(Locks(line));
    var timeout = TimeSpan.FromSeconds(line.Number("timeout-seconds", 30, 900, 240));
    var gatewayKey = GatewayKey();
    var judge = Claude(RelevanceJudge.Model);

    var transport = new ResearchTransport(new CallBudget(0, 0, 0), new CallBudget(maxClaude, RelevanceJudge.Ceiling, JsonLines.Load<JudgeLine>(outPath).Count));
    using var http = new HttpClient { Timeout = timeout + TimeSpan.FromSeconds(60) };
    var (client, model, _) = new GatewayResolver(http, gateway, gatewayKey).ForProfile(judge.Provider, judge.Model, 4_000);
    Console.Error.WriteLine($"Judge {judge.Provider}/{model}, blind (topics and passages only), failover off.");
    return await new RelevanceJudgeRunner(outPath, client, model, timeout, transport, guard, TimeProvider.System, Console.Error, [gatewayKey])
        .RunAsync(JsonLines.Load<ResearchRunLine>(runPath), ct);
}

static int Evaluate(CommandLine line)
{
    var orders = LoadOrders(line);
    var settings = ResearchKit.Settings(Settings(line));
    var holdout = Read(line.Required("holdout"));
    var also = line.All("also").Select(Read).ToList();
    var judgments = JsonLines.Load<JudgeLine>(line.Required("judgments"));
    var verificationPath = line.Required("verification");
    var verification = File.Exists(verificationPath) ? JsonSerializer.Deserialize<VerificationReport>(File.ReadAllText(verificationPath)) : null;

    var (verdicts, figures) = ResearchEvaluation.Evaluate(orders, holdout, also, judgments, verification, ResearchKit.Real, settings);
    foreach (var figure in figures)
        Console.WriteLine(figure);
    foreach (var verdict in verdicts)
        Console.WriteLine(verdict);
    return ResearchEvaluation.ExitCode(verdicts[^1].Verdict);

    static RunFile Read(string path) =>
        File.Exists(path) ? new RunFile(path, JsonLines.FileSha256(path), JsonLines.Load<ResearchRunLine>(path)) : throw new UsageException($"{path} does not exist");
}
