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
/// Runs the real <see cref="LlmClarifier"/> and <see cref="QuestionGate"/> over a corpus and appends one result line per call.
/// Resumable: entries whose last result for the current input is not a failure are not called again; failed calls stay in the file
/// and count in the figures of <see cref="CorpusEvaluation"/>, so repeating a run never improves them. Results made for another
/// input (changed request, files, profile, prompt or schema) are never taken as done; they leave the file not evaluable.
/// </summary>
internal sealed class EvalRunner(RunSettings settings, TextWriter log)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim _write = new(1, 1);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var options = new IntakeOptions { Timeout = settings.Timeout };
        var corpus = Corpus.Load(settings.CorpusPath);
        var previous = Results.Load(settings.OutPath);
        var done = CorpusEvaluation.Answered(corpus, previous);
        var stale = CorpusEvaluation.OtherInput(corpus, previous).Select(l => l.Id).Distinct(StringComparer.Ordinal).Count();
        if (stale > 0)
            log.WriteLine($"Warning: {stale} entries in {settings.OutPath} have results for another input; they are called again, but the file stays not evaluable. Use a new --out file.");
        var entries = corpus
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
            DateTimeOffset.UtcNow,
            (int)options.Timeout.TotalMilliseconds,
            entry.InputHash(options));
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
}
