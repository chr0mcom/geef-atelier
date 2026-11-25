using System.Text.Json.Serialization;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>A question as it would be shown, in the result file.</summary>
public sealed record ResultQuestion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("options")] IReadOnlyList<string> Options);

/// <summary>One gate decision, in the result file.</summary>
public sealed record ResultTrace(
    [property: JsonPropertyName("gap_index")] int GapIndex,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("decision")] string Decision);

/// <summary>
/// One line of <c>results-*.jsonl</c>: latency, model, raw answer, gate trace and questions of one call for a corpus entry, the
/// time limit the call ran with (<see cref="TimeoutMs"/>; the evaluation counts an answer over the production limit as a failure)
/// and the fingerprint of the input (<see cref="InputHash"/>, see <see cref="CorpusEvaluation.InputHash"/>; a result for another
/// input makes the file not evaluable).
/// </summary>
public sealed record EvalResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("corpus")] string Corpus,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("raw")] string? Raw,
    [property: JsonPropertyName("question_count")] int QuestionCount,
    [property: JsonPropertyName("questions")] IReadOnlyList<ResultQuestion> Questions,
    [property: JsonPropertyName("trace")] IReadOnlyList<ResultTrace> Trace,
    [property: JsonPropertyName("research_topics")] IReadOnlyList<string> ResearchTopics,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("timeout_ms")] int? TimeoutMs = null,
    [property: JsonPropertyName("input_hash")] string? InputHash = null);

/// <summary>Reading a result file.</summary>
public static class Results
{
    /// <summary>All lines of a result file in their order; a missing file has none.</summary>
    public static IReadOnlyList<EvalResult> Load(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => System.Text.Json.JsonSerializer.Deserialize<EvalResult>(l)!).ToList()
            : [];
}
