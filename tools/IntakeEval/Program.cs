using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Tools.IntakeEval;

const string Usage = """
    Usage: IntakeEval run --corpus <file.jsonl> --proxy <url> --out <results.jsonl> [--max-calls N] [--parallel 2]
                          [--model <name>] [--split all|dev|holdout] [--timeout-seconds 40]
           IntakeEval evaluate --corpus <main.jsonl> --second <second.jsonl> --results <results.jsonl>
                          [--production-timeout-seconds 40] [--drop-guessed-dates false|true]
           IntakeEval shadow --csv <shadows.csv> [--sample 10]

    run       Runs the real clarifier and question gate over a synthetic corpus and appends one JSON line per call
              (latency, model, raw answer, gate trace, questions, time limit, input hash). Resumable: entries whose
              last result for the current input is Ok or Invalid are skipped; failed calls stay in the file and count
              in the evaluation. Results for another input are called again and leave the file not evaluable.
              Example: --proxy http://cli-proxy:8090/v1/claude
    evaluate  Replays the stored answers and prints passed / FAILED / NOT EVALUABLE for gates a to f (and the warning e')
              on the held-out set and the second corpus. Exit code 0 only when every gate passed. With
              --drop-guessed-dates true the gate drops answer options naming a calendar day when nothing in the request,
              the files or the profile looks like a date (Intake:DropGuessedDateOptions); the stored answers stay valid.
              The first line names the mode and how many questions and options the rule drops in these results.
    shadow    Reads a CSV export of OrderIntakeShadows (see README.md) and prints gates a, f and g of the shadow mode
              and a sample of questions for the visual check. Exit code 0 only when every measurable gate passed.
    """;

if (args.Length == 0 || args[0] is not ("run" or "evaluate" or "shadow"))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string[] known = args[0] switch
{
    "run" => ["corpus", "proxy", "out", "max-calls", "parallel", "model", "split", "timeout-seconds"],
    "evaluate" => ["corpus", "second", "results", "production-timeout-seconds", "drop-guessed-dates"],
    _ => ["csv", "sample"],
};
var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || !known.Contains(args[i][2..], StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
        Console.Error.WriteLine(Usage);
        return 2;
    }
    values[args[i][2..]] = args[i + 1];
}

int Number(string name, int fallback) =>
    int.Parse(values.GetValueOrDefault(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);

var productionLimit = TimeSpan.FromSeconds(Number("production-timeout-seconds", (int)new IntakeOptions().Timeout.TotalSeconds));

if (args[0] == "evaluate")
{
    if (!values.TryGetValue("corpus", out var mainPath) || !values.TryGetValue("second", out var secondPath) || !values.TryGetValue("results", out var resultsPath))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
    if (values.GetValueOrDefault("drop-guessed-dates", "false") is not ("true" or "false") and var flag)
    {
        Console.Error.WriteLine($"--drop-guessed-dates takes true or false, not {flag}");
        Console.Error.WriteLine(Usage);
        return 2;
    }
    var dropGuessedDates = values.GetValueOrDefault("drop-guessed-dates") == "true";
    var corpusEntries = Corpus.Load(mainPath).Concat(Corpus.Load(secondPath)).ToList();
    var lines = Results.Load(resultsPath);
    var effect = CorpusEvaluation.DateRuleEffect(corpusEntries, lines);
    Console.WriteLine($"Date rule (Intake:DropGuessedDateOptions): {(dropGuessedDates ? "on" : "off")}; with the rule on it would drop " +
        $"{effect.Questions} questions and {effect.Options} answer options of these results.");
    var verdicts = CorpusEvaluation.Verdicts(corpusEntries, lines, productionLimit, dropGuessedDates);
    foreach (var verdict in verdicts)
        Console.WriteLine(verdict);
    return verdicts.Where(v => v.Gate != "e'").All(v => v.Verdict == GateVerdict.Passed) ? 0 : 1;
}

if (args[0] == "shadow")
{
    if (!values.TryGetValue("csv", out var csvPath))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
    using var reader = new StreamReader(csvPath);
    var rows = ShadowEvaluation.Read(reader);
    var figures = ShadowEvaluation.Measure(rows);
    var verdicts = ShadowEvaluation.Verdicts(figures);
    foreach (var verdict in verdicts)
        Console.WriteLine(verdict);
    Console.WriteLine("Questions for the visual check:");
    foreach (var question in ShadowEvaluation.Sample(rows, Number("sample", 10)))
        Console.WriteLine($"- {question}");
    return verdicts.Where(v => v.Scope != "visual check").All(v => v.Verdict == GateVerdict.Passed) ? 0 : 1;
}

if (!values.TryGetValue("corpus", out var corpus) || !values.TryGetValue("proxy", out var proxy) || !values.TryGetValue("out", out var output))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var split = values.GetValueOrDefault("split", "all");
if (split is not ("all" or "dev" or "holdout"))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var settings = new RunSettings(
    corpus,
    proxy,
    output,
    Number("max-calls", 160),
    Number("parallel", 2),
    values.GetValueOrDefault("model", ModelMatrix.Primary(ModelSlot.Intake).Model),
    split,
    TimeSpan.FromSeconds(Number("timeout-seconds", 40)));

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

return await new EvalRunner(settings, Console.Error).RunAsync(stop.Token);
