using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Infrastructure.Intake;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>Settings of one measuring run.</summary>
internal sealed record RunSettings(string CorpusPath, string Proxy, string OutPath, int MaxCalls, int Parallel, string Model, string Split, TimeSpan Timeout);

/// <summary>
/// Runs the real <see cref="LlmClarifier"/> and <see cref="QuestionGate"/> over a corpus and appends one result line per entry.
/// Resumable: entries with a result other than a failure are not called again.
/// </summary>
internal sealed class EvalRunner(RunSettings settings, TextWriter log)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim _write = new(1, 1);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var options = new IntakeOptions { Timeout = settings.Timeout };
        var done = Done(settings.OutPath);
        var entries = Corpus.Load(settings.CorpusPath)
            .Where(e => settings.Split == "all" || e.Split() == settings.Split)
            .Where(e => !done.Contains(e.Id))
            .Take(settings.MaxCalls)
            .ToList();
        log.WriteLine($"{entries.Count} entries to call ({done.Count} already done, cap {settings.MaxCalls}).");

        using var http = new HttpClient { Timeout = settings.Timeout + TimeSpan.FromSeconds(30) };
        var client = new Infrastructure.Llm.OpenAiCompatibleClient(http, settings.Proxy, "intake-eval");
        var clarifier = new LlmClarifier(
            new FixedModelAssignment(new Core.Domain.Crew.Models.ModelCandidate("claude-cli", settings.Model)),
            new ProxyResolver(client), Options.Create(options), NullLogger<LlmClarifier>.Instance, TimeProvider.System);

        using var slots = new SemaphoreSlim(Math.Max(1, settings.Parallel));
        var failures = 0;
        await Task.WhenAll(entries.Select(async entry =>
        {
            await slots.WaitAsync(ct);
            try
            {
                var result = await EvaluateAsync(entry, clarifier, options, ct);
                if (result.Status != nameof(IntakeRecordStatus.Ok))
                    Interlocked.Increment(ref failures);
                await AppendAsync(result, ct);
                log.WriteLine($"{entry.Id}: {result.Status} {result.DurationMs} ms, {result.QuestionCount} questions");
            }
            finally
            {
                slots.Release();
            }
        }));

        log.WriteLine($"Finished: {entries.Count - failures} ok, {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<EvalResult> EvaluateAsync(CorpusEntry entry, LlmClarifier clarifier, IntakeOptions options, CancellationToken ct)
    {
        var request = entry.Request(options);
        var call = await clarifier.ClarifyAsync(request, ct);
        var gate = call.Output is { } output ? entry.Gate(request, output, options).Gate : null;
        return new EvalResult(
            entry.Id, entry.Corpus, entry.Category, entry.Lang, entry.Split(), call.Status.ToString(), call.Model, call.DurationMs, call.ErrorCode,
            call.RawJson,
            gate?.Questions.Count ?? 0,
            gate?.Questions.Select(q => new ResultQuestion(q.Id, q.Slot.ToString(), q.Text, q.Options.Select(o => o.Label).ToList())).ToList() ?? [],
            gate?.Trace.Select(t => new ResultTrace(t.GapIndex, t.Topic.ToString(), t.Decision.ToString())).ToList() ?? [],
            gate?.ResearchTopics ?? [],
            DateTimeOffset.UtcNow);
    }

    private async Task AppendAsync(EvalResult result, CancellationToken ct)
    {
        await _write.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(settings.OutPath, JsonSerializer.Serialize(result, Json) + "\n", ct);
        }
        finally
        {
            _write.Release();
        }
    }

    private static HashSet<string> Done(string path)
    {
        if (!File.Exists(path))
            return [];
        return File.ReadLines(path)
            .Where(l => l.TrimStart().StartsWith('{'))
            .Select(l => JsonSerializer.Deserialize<EvalResult>(l)!)
            .GroupBy(r => r.Id)
            .Where(g => g.Last().Status is nameof(IntakeRecordStatus.Ok) or nameof(IntakeRecordStatus.Invalid))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}
