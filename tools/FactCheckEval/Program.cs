using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Tools.FactCheckEval;
using Microsoft.Extensions.Logging.Abstractions;

const string Usage = """
    Usage: FactCheckEval probe-notes --inputs <orders.jsonl> --gateway <url> --out <results.jsonl> --short N --long M
                                     --max-calls N --codex-lock <file> [--codex-lock <file> ...] [--timeout-seconds 1800]
           FactCheckEval probe-evaluate --inputs <orders.jsonl> --results <results.jsonl>
           FactCheckEval run --corpus <factcheck-corpus.jsonl> --gateway <url> --out <results.jsonl> --split dev|holdout|all
                             --max-calls N --codex-lock <file> [--codex-lock <file> ...] [--timeout-seconds 90] [--reasoning-effort low]
           FactCheckEval evaluate --corpus <factcheck-corpus.jsonl> --results <results.jsonl> [--production-timeout-seconds 90]
           FactCheckEval arithmetic --corpus <arithmetic-corpus.jsonl>

    probe-notes     V1: sends the real first writing pass (Opus, document mode, notes block asked for) for the probe orders until the
                    file holds N short and M long attempts (cumulative; stage a: 4/2, stage b: 10/5, stage c: 10/10). Appends one line
                    per attempt. Example gateway: http://cli-proxy:8090 (the route /v1/claude or /v1/codex follows from the model).
    probe-evaluate  V1: re-splits the stored answers with the current WriterNotesFormat and prints outcome A, B or C.
    run             V2: calls the real fact checker (model as ModelChooser picks it next to the Opus writer) for every text of the split,
                    one at a time, and appends one line per attempt. Resumable; failed attempts stay and count.
    evaluate        V2: replays the stored answers through parser and filter, prints the development figures and gate V2 on the
                    held-out set (with the mandatory parents' evening).
    arithmetic      V3: runs SourceArithmeticCheck over the arithmetic corpus and prints gate V3 on the held-out set.

    --max-calls is required: the cap of this invocation. A result file never holds more than 20 (probe-notes) or 52 (run) attempts.
    Before every call the tool stops (exit 3) while a --codex-lock file exists or `pgrep -a codex` finds a process.
    The gateway key, if the gateway needs one, comes only from the environment variable FACTCHECK_EVAL_API_KEY.
    Exit codes: 0 done / passed / decided, 1 gate failed, 2 wrong usage, 3 stopped by the Codex guard, 4 incomplete or not evaluable.
    """;

string[] commands = ["probe-notes", "probe-evaluate", "run", "evaluate", "arithmetic"];
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
        "probe-notes" => await ProbeNotesAsync(Parse("inputs", "gateway", "out", "short", "long", "max-calls", "codex-lock", "timeout-seconds"), stop.Token),
        "probe-evaluate" => ProbeEvaluate(Parse("inputs", "results")),
        "run" => await RunAsync(Parse("corpus", "gateway", "out", "split", "max-calls", "codex-lock", "timeout-seconds", "reasoning-effort"), stop.Token),
        "evaluate" => Evaluate(Parse("corpus", "results", "production-timeout-seconds")),
        _ => Arithmetic(Parse("corpus")),
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
    Console.Error.WriteLine("A part of the production code this command needs is not implemented on this branch yet (see tools/FactCheckEval/README.md for the merge order).");
    return 4;
}

CommandLine Parse(params string[] allowed) => CommandLine.Parse(args[1..], allowed);

static IReadOnlyList<string> Locks(CommandLine line)
{
    var locks = line.All("codex-lock");
    return locks.Count > 0 ? locks : throw new UsageException("--codex-lock is required (path of the lock file of the shared Codex subscription)");
}

static HttpClient Http(TimeSpan timeout) => new() { Timeout = timeout + TimeSpan.FromSeconds(60) };

static async Task<int> ProbeNotesAsync(CommandLine line, CancellationToken ct)
{
    var settings = new ProbeSettings(
        line.Required("inputs"), line.Required("out"),
        line.Number("short", 0, NotesProbeRunner.Ceiling), line.Number("long", 0, NotesProbeRunner.Ceiling),
        line.Number("max-calls", 1, NotesProbeRunner.Ceiling),
        TimeSpan.FromSeconds(line.Number("timeout-seconds", 60, 3600, 1800)));
    var guard = CodexGuard.ForHost(Locks(line));
    var writer = NotesProbe.Writer;
    using var http = Http(settings.Timeout);
    var resolver = new GatewayResolver(http, line.Required("gateway"), CommandLine.ApiKey(Environment.GetEnvironmentVariable));
    var (client, model, _) = resolver.ForProfile(writer.Provider, writer.Model, writer.MaxTokens);
    Console.Error.WriteLine($"Writer {writer.Provider}/{model}, document mode, proxy failover off.");
    return await new NotesProbeRunner(settings, client, o => NotesProbe.Request(o, writer), guard, raw => WriterNotesFormat.Split(raw), Console.Error).RunAsync(ct);
}

static int ProbeEvaluate(CommandLine line)
{
    var orders = ResultFile.Load<ProbeOrder>(line.Required("inputs"));
    var hashes = orders.ToDictionary(o => o.Id, o => NotesProbe.InputHash(NotesProbe.Request(o, NotesProbe.Writer)), StringComparer.Ordinal);
    var lines = NotesProbeEvaluation.Reread(ResultFile.Load<ProbeLine>(line.Required("results")), raw => WriterNotesFormat.Split(raw));
    var (outcome, detail) = NotesProbeEvaluation.Decide(lines, NotesProbeEvaluation.Problems(lines, hashes));
    Console.WriteLine($"V1 outcome {(outcome == ProbeOutcome.NotEvaluable ? "NOT EVALUABLE" : outcome.ToString())}: {detail}");
    return outcome == ProbeOutcome.NotEvaluable ? 4 : 0;
}

static async Task<int> RunAsync(CommandLine line, CancellationToken ct)
{
    var split = line.Required("split");
    if (split is not (EvalBasics.Dev or EvalBasics.Holdout or "all"))
        throw new UsageException("--split must be dev, holdout or all");
    var effort = line.Optional("reasoning-effort");
    var settings = new FactCheckRunSettings(
        line.Required("corpus"), line.Required("out"), split,
        line.Number("max-calls", 1, FactCheckRunner.Ceiling),
        TimeSpan.FromSeconds(line.Number("timeout-seconds", 10, 1800, (int)new DirectOptions().FactCheckTimeout.TotalSeconds)),
        effort);
    var guard = CodexGuard.ForHost(Locks(line));

    var executor = ModelMatrix.Primary(ModelSlot.Executor);
    var chosen = ModelChooser.Choose(
        ModelMatrix.CandidatesFor(ModelSlot.FactReviewer),
        new ModelChoiceContext(ModelSlot.FactReviewer, ModelDomain.General, executor, 1, 0, new Dictionary<ModelCandidate, int>()),
        _ => CandidateAvailability.Unknown,
        c => c == executor).Candidate;
    var profile = SystemCrew.SourceFactCheckProfile with { Provider = chosen.Provider, Model = chosen.Model };
    Console.Error.WriteLine($"Fact checker {profile.Provider}/{profile.Model} next to the writer {executor.Provider}/{executor.Model}; prompt {FactCheckPrompts.Version}.");

    var options = new DirectOptions { FactCheckTimeout = settings.Timeout, FactCheckReasoningEffort = effort };
    using var http = Http(settings.Timeout);
    var resolver = new GatewayResolver(http, line.Required("gateway"), CommandLine.ApiKey(Environment.GetEnvironmentVariable));
    var checker = new LlmFactChecker(resolver, options, null, null, NullLogger<LlmFactChecker>.Instance);
    return await new FactCheckRunner(settings, checker, profile, guard, FactCheckKit.Real, Console.Error).RunAsync(ct);
}

static int Evaluate(CommandLine line)
{
    var bases = FactCheckCorpus.Load(line.Required("corpus"));
    var lines = ResultFile.Load<FactCheckLine>(line.Required("results"));
    var limit = TimeSpan.FromSeconds(line.Number("production-timeout-seconds", 10, 1800, (int)new DirectOptions().FactCheckTimeout.TotalSeconds));
    if (FactCheckEvaluation.DataProblems(bases, lines, FactCheckKit.Real) is { Count: 0 })
        Console.WriteLine(FactCheckEvaluation.DevelopmentFigures(bases, lines, FactCheckKit.Real, limit));
    var verdicts = FactCheckEvaluation.Verdicts(bases, lines, FactCheckKit.Real, limit);
    foreach (var verdict in verdicts)
        Console.WriteLine(verdict);
    return ExitCode(verdicts[^1].Verdict);
}

static int Arithmetic(CommandLine line)
{
    var verdicts = ArithmeticCorpusEvaluation.Verdicts(ArithmeticCorpusEvaluation.Load(line.Required("corpus")), Geef.Atelier.Core.Domain.Checks.SourceArithmeticCheck.Check);
    foreach (var verdict in verdicts)
        Console.WriteLine(verdict);
    return ExitCode(verdicts.Single(v => v.Gate == ArithmeticCorpusEvaluation.Gate).Verdict);
}

static int ExitCode(GateVerdict verdict) => verdict switch
{
    GateVerdict.Passed => 0,
    GateVerdict.Failed => 1,
    _ => 4,
};
