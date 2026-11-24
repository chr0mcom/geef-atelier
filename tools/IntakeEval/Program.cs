using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Tools.IntakeEval;

const string Usage = """
    Usage: IntakeEval run --corpus <file.jsonl> --proxy <url> --out <results.jsonl> [--max-calls N] [--parallel 2]
                          [--model <name>] [--split all|dev|holdout] [--timeout-seconds 40]

    Runs the real clarifier and question gate over a synthetic corpus and appends one JSON line per entry
    (latency, model, raw answer, gate trace, questions). Resumable: entries with status Ok or Invalid are skipped.
    Example: --proxy http://cli-proxy:8090/v1/claude
    """;

if (args.Length == 0 || args[0] != "run")
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
    values[args[i][2..]] = args[i + 1];
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
    int.Parse(values.GetValueOrDefault("max-calls", "160"), System.Globalization.CultureInfo.InvariantCulture),
    int.Parse(values.GetValueOrDefault("parallel", "2"), System.Globalization.CultureInfo.InvariantCulture),
    values.GetValueOrDefault("model", ModelMatrix.Primary(ModelSlot.Intake).Model),
    split,
    TimeSpan.FromSeconds(int.Parse(values.GetValueOrDefault("timeout-seconds", "40"), System.Globalization.CultureInfo.InvariantCulture)));

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

return await new EvalRunner(settings, Console.Error).RunAsync(stop.Token);
