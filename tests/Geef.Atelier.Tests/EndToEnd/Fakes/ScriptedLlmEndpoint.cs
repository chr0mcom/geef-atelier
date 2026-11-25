using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Infrastructure.Research;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>How the fake reviewers answer <c>submit_review</c>.</summary>
internal enum ReviewerVerdict
{
    /// <summary>Approve with one non-blocking <c>info</c> finding (an empty approval triggers a reviewer retry).</summary>
    ApproveWithNote,

    /// <summary>Always report one blocking <c>major</c> finding, so the crew never converges.</summary>
    AlwaysBlock,
}

/// <summary>
/// Scriptable stand-in for the cli-proxy's OpenAI-compatible <c>/v1/{cli}/chat/completions</c> endpoint
/// (and OpenRouter's, which shares the format). It records every request and answers each role plausibly:
/// the executor returns a draft, reviewers call <c>submit_review</c>, the grounding refiner keeps every
/// source, the query extractor returns a search query, the research condenser (<c>pick_passages</c>) picks no passage unless scripted.
/// </summary>
internal sealed class ScriptedLlmEndpoint(LlmTrafficRecorder recorder)
{
    public const string DefaultExecutorText =
        "E2E draft. This text was produced by the scripted executor of the end-to-end test.";

    private static readonly string ExecutorPromptPrefix = SystemPrompts.Executor.Trim()[..60];

    /// <summary>Text the executor returns in every iteration.</summary>
    public string ExecutorText { get; set; } = DefaultExecutorText;

    /// <summary>Reviewer behaviour for all reviewer calls.</summary>
    public ReviewerVerdict ReviewerVerdict { get; set; } = ReviewerVerdict.ApproveWithNote;

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string? Text, int Status, string? Code)> _executorAnswers = new();

    /// <summary>Queues the text of the next executor answer; once the queue is empty <see cref="ExecutorText"/> is used.</summary>
    public void EnqueueExecutorText(string text) => _executorAnswers.Enqueue((text, 200, null));

    /// <summary>Queues an error answer for the next executor call: the cli-proxy error envelope with <paramref name="code"/>.</summary>
    public void EnqueueExecutorError(int status, string code) => _executorAnswers.Enqueue((null, status, code));

    private readonly System.Collections.Concurrent.ConcurrentQueue<ExecutorHold> _executorHolds = new();

    /// <summary>
    /// Holds the next executor call open until the test answers it through the returned handle or the caller gives up; a held call
    /// is served before the queued executor answers.
    /// </summary>
    public ExecutorHold HoldNextExecutor()
    {
        var hold = new ExecutorHold();
        _executorHolds.Enqueue(hold);
        return hold;
    }

    /// <summary>Name of the response schema by which the clarifier call is recognised.</summary>
    public const string ClarifierSchemaName = "clarify_order";

    /// <summary>Clarifier answer once the queue is empty: a valid analysis without gaps, so the order starts without questions.</summary>
    public const string DefaultClarifierJson =
        """{"text_type":"Text","language":"de","understanding":"Ein Text wie beschrieben.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[],"research_topics":[]}""";

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string? Json, int Status, TimeSpan Delay)> _clarifierAnswers = new();

    /// <summary>Queues the JSON content of the next clarifier answer.</summary>
    public void EnqueueClarifierJson(string json) => _clarifierAnswers.Enqueue((json, 200, TimeSpan.Zero));

    /// <summary>Queues an error answer (without <c>Retry-After</c>) for the next clarifier call.</summary>
    public void EnqueueClarifierError(int status) => _clarifierAnswers.Enqueue((null, status, TimeSpan.Zero));

    /// <summary>Queues a clarifier answer that comes only after <paramref name="delay"/> (then <see cref="DefaultClarifierJson"/>).</summary>
    public void EnqueueClarifierDelay(TimeSpan delay) => _clarifierAnswers.Enqueue((DefaultClarifierJson, 200, delay));

    /// <summary>True for a request of the clarifier (structured output or forced tool named <see cref="ClarifierSchemaName"/>).</summary>
    public static bool IsClarifierCall(RecordedLlmCall call) => IsClarifierBody(call.RawBody);

    /// <summary>Fact-check answer once the queue is empty: no findings.</summary>
    public const string DefaultFactCheckJson = """{"findings":[]}""";

    /// <summary>Recheck answer once the queue is empty: no results (every asked finding counts as unclear).</summary>
    public const string DefaultRecheckJson = """{"results":[]}""";

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string? Json, int Status, string? Code)> _factCheckAnswers = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _recheckAnswers = new();

    /// <summary>Queues the JSON content of the next <c>check_facts</c> answer.</summary>
    public void EnqueueFactCheckJson(string json) => _factCheckAnswers.Enqueue((json, 200, null));

    /// <summary>Queues the JSON content of the next <c>recheck_facts</c> answer.</summary>
    public void EnqueueRecheckJson(string json) => _recheckAnswers.Enqueue(json);

    /// <summary>Queues an error answer (cli-proxy error envelope with <paramref name="code"/>, without <c>Retry-After</c>) for the next <c>check_facts</c> call.</summary>
    public void EnqueueFactCheckError(int status, string code = "scripted") => _factCheckAnswers.Enqueue((null, status, code));

    private const int HangStatus = -1;

    /// <summary>Queues a <c>check_facts</c> call that never answers: it waits until the caller gives up (its own time limit).</summary>
    public void EnqueueFactCheckHang() => _factCheckAnswers.Enqueue((null, HangStatus, null));

    /// <summary>The value of <c>x-cli-proxy-timeout-seconds</c> of every <c>check_facts</c> call, in order (<c>null</c> without the header).</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string?> FactCheckTimeoutHeaders { get; } = new();

    /// <summary>True for a <c>check_facts</c> request (structured output named <see cref="FactCheckSchema.CheckName"/>).</summary>
    public static bool IsFactCheckCall(RecordedLlmCall call) => SchemaName(call.RawBody) == FactCheckSchema.CheckName;

    /// <summary>True for a <c>recheck_facts</c> request (structured output named <see cref="FactCheckSchema.RecheckName"/>).</summary>
    public static bool IsRecheckCall(RecordedLlmCall call) => SchemaName(call.RawBody) == FactCheckSchema.RecheckName;

    /// <summary>Answer of the research condenser once the queue is empty: no passage.</summary>
    public const string DefaultPassagesJson = """{"passages":[]}""";

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string? Json, int Status, string? Code)> _passagesAnswers = new();

    /// <summary>Queues the JSON content of the next <c>pick_passages</c> answer.</summary>
    public void EnqueuePassagesJson(string json) => _passagesAnswers.Enqueue((json, 200, null));

    /// <summary>Queues an error answer (cli-proxy error envelope with <paramref name="code"/>) for the next <c>pick_passages</c> call.</summary>
    public void EnqueuePassagesError(int status, string code = "scripted") => _passagesAnswers.Enqueue((null, status, code));

    /// <summary>When set, answers every <c>pick_passages</c> call whose queue is empty from the recorded request (for example with the page ids it names).</summary>
    public Func<RecordedLlmCall, string>? PassagesResponder { get; set; }

    /// <summary>True for a request of the research condenser (structured output named <see cref="DossierSchema.Name"/>).</summary>
    public static bool IsPassagesCall(RecordedLlmCall call) => SchemaName(call.RawBody) == DossierSchema.Name;

    /// <summary>Input tokens reported for executor answers; <c>null</c> keeps the default estimate (prompt characters / 4).</summary>
    public int? ExecutorPromptTokens { get; set; }

    /// <summary>Handles one HTTP request sent through the named <c>llm</c> client.</summary>
    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (request.Method == HttpMethod.Get && url.EndsWith("/models", StringComparison.Ordinal))
            return Json(ModelList(ModelMatrix.All.Select(c => c.Model)));

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var call = Parse(recorder.NextIndex(), request.Method.Method, url, body);
        recorder.Add(call);

        if (IsClarifierBody(body))
            return await ClarifierAsync(call, cancellationToken);

        switch (SchemaName(body))
        {
            case FactCheckSchema.CheckName:
                FactCheckTimeoutHeaders.Enqueue(request.Headers.TryGetValues("x-cli-proxy-timeout-seconds", out var limits) ? limits.Single() : null);
                var (factJson, factStatus, factCode) = _factCheckAnswers.TryDequeue(out var fact) ? fact : (DefaultFactCheckJson, 200, null);
                if (factStatus == HangStatus)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new OperationCanceledException(cancellationToken);
                }
                return factCode is not null
                    ? PlainError(factStatus, factCode)
                    : Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = factJson }, "stop"));
            case FactCheckSchema.RecheckName:
                var recheckJson = _recheckAnswers.TryDequeue(out var recheck) ? recheck : DefaultRecheckJson;
                return Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = recheckJson }, "stop"));
            case DossierSchema.Name:
                var (passagesJson, passagesStatus, passagesCode) = _passagesAnswers.TryDequeue(out var passages)
                    ? passages
                    : (PassagesResponder?.Invoke(call) ?? DefaultPassagesJson, 200, null);
                return passagesCode is not null
                    ? PlainError(passagesStatus, passagesCode)
                    : Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = passagesJson }, "stop"));
        }

        if (call.Role == LlmRole.Executor && _executorHolds.TryDequeue(out var hold))
        {
            var (heldText, heldStatus, heldCode) = await hold.WaitAsync(cancellationToken);
            return heldCode is not null
                ? ErrorEnvelope(heldStatus, heldCode)
                : Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = heldText }, "stop", ExecutorPromptTokens));
        }

        if (call.Role == LlmRole.Executor && _executorAnswers.TryDequeue(out var answer))
        {
            if (answer.Code is { } code)
                return ErrorEnvelope(answer.Status, code);
            return Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = answer.Text }, "stop", ExecutorPromptTokens));
        }

        return Json(Respond(call));
    }

    private async Task<HttpResponseMessage> ClarifierAsync(RecordedLlmCall call, CancellationToken cancellationToken)
    {
        var (json, status, delay) = _clarifierAnswers.TryDequeue(out var queued) ? queued : (DefaultClarifierJson, 200, TimeSpan.Zero);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
        if (status != 200)
        {
            var payload = new JsonObject
            {
                ["error"] = new JsonObject { ["message"] = $"E2E scripted clarifier error {status}.", ["type"] = "api_error", ["code"] = "scripted" },
            };
            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
        return Json(Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = json }, "stop"));
    }

    private static bool IsClarifierBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.Contains(ClarifierSchemaName, StringComparison.Ordinal))
            return false;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("response_format", out var format) && format.ValueKind == JsonValueKind.Object
            && format.TryGetProperty("json_schema", out var schema) && schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("name", out var name) && name.GetString() == ClarifierSchemaName)
            return true;
        return root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array
            && tools.EnumerateArray().Any(t => t.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var n) && n.GetString() == ClarifierSchemaName);
    }

    private static string? SchemaName(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.Contains("response_format", StringComparison.Ordinal))
            return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("response_format", out var format) && format.ValueKind == JsonValueKind.Object
            && format.TryGetProperty("json_schema", out var schema) && schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
    }

    private static HttpResponseMessage PlainError(int status, string code)
    {
        var payload = new JsonObject
        {
            ["error"] = new JsonObject { ["message"] = $"E2E scripted error {code}.", ["type"] = "api_error", ["code"] = code },
        };
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage ErrorEnvelope(int status, string code)
    {
        var payload = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = $"E2E scripted cli-proxy error {code}.",
                ["type"] = "api_error",
                ["code"] = code,
            },
        };
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (status == 503)
            response.Headers.Add("Retry-After", "300");
        return response;
    }

    internal static JsonObject ModelList(IEnumerable<string> ids) => new()
    {
        ["object"] = "list",
        ["data"] = new JsonArray(ids.Distinct().Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray()),
        ["x_source"] = "live",
    };

    private JsonObject Respond(RecordedLlmCall call) => call.Role switch
    {
        LlmRole.Reviewer => ToolCallResponse(call, "submit_review", ReviewArguments()),
        LlmRole.Refiner => ToolCallResponse(call, "submit_refinement", RefinementArguments(call)),
        LlmRole.QueryExtractor => TextResponse(call, "e2e lighthouse history search"),
        LlmRole.Executor => Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = ExecutorText }, "stop", ExecutorPromptTokens),
        _ when call.ToolChoice is { } choice && choice.StartsWith("function:", StringComparison.Ordinal)
            => ToolCallResponse(call, choice["function:".Length..], new JsonObject()),
        _ => TextResponse(call, "E2E generic answer."),
    };

    private JsonObject ReviewArguments() => ReviewerVerdict switch
    {
        ReviewerVerdict.AlwaysBlock => new JsonObject
        {
            ["approved"] = false,
            ["findings"] = new JsonArray(new JsonObject
            {
                ["severity"] = "major",
                ["message"] = "E2E: the draft misses the section the briefing requires.",
            }),
        },
        _ => new JsonObject
        {
            ["approved"] = true,
            ["findings"] = new JsonArray(new JsonObject
            {
                ["severity"] = "info",
                ["message"] = "E2E: optional note, no action required.",
            }),
        },
    };

    private static JsonObject RefinementArguments(RecordedLlmCall call)
    {
        if (call.HasSynthesizeSchema)
        {
            return new JsonObject
            {
                ["synthesized_text"] = call.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty,
                ["referenced_indices"] = new JsonArray(Enumerable.Range(0, 10).Select(i => (JsonNode)i).ToArray()),
            };
        }

        return new JsonObject
        {
            ["sources"] = new JsonArray(Enumerable.Range(0, 10)
                .Select(i => (JsonNode)new JsonObject { ["index"] = i, ["keep"] = true, ["reason"] = "relevant" })
                .ToArray()),
        };
    }

    private static JsonObject TextResponse(RecordedLlmCall call, string text) =>
        Envelope(call, new JsonObject { ["role"] = "assistant", ["content"] = text }, "stop");

    private static JsonObject ToolCallResponse(RecordedLlmCall call, string toolName, JsonObject arguments) =>
        Envelope(call, new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = null,
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = $"call_e2e_{call.Index}",
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolName,
                    ["arguments"] = arguments.ToJsonString(),
                },
            }),
        }, "tool_calls");

    private static JsonObject Envelope(RecordedLlmCall call, JsonObject message, string finishReason, int? promptTokens = null) => new()
    {
        ["id"] = $"chatcmpl-e2e-{call.Index}",
        ["object"] = "chat.completion",
        ["model"] = call.Model,
        ["choices"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["message"] = message,
            ["finish_reason"] = finishReason,
        }),
        ["usage"] = new JsonObject
        {
            ["prompt_tokens"] = promptTokens ?? Math.Max(1, call.PromptText.Length / 4),
            ["completion_tokens"] = 42,
            ["total_tokens"] = (promptTokens ?? Math.Max(1, call.PromptText.Length / 4)) + 42,
        },
    };

    private static HttpResponseMessage Json(JsonNode payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    internal static RecordedLlmCall Parse(int index, string method, string url, string body)
    {
        string? model = null;
        var messages = new List<RecordedMessage>();
        var toolNames = new List<string>();
        string? toolChoice = null;
        var documentMode = false;
        string? document = null;
        string? contextDocument = null;
        var hasSynthesizeSchema = false;

        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in msgs.EnumerateArray())
                {
                    var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                    var content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()
                        : null;
                    messages.Add(new RecordedMessage(role, content));
                }
            }

            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    if (!tool.TryGetProperty("function", out var fn))
                        continue;
                    if (fn.TryGetProperty("name", out var name) && name.GetString() is { } toolName)
                        toolNames.Add(toolName);
                    if (fn.TryGetProperty("parameters", out var parameters)
                        && parameters.ValueKind == JsonValueKind.Object
                        && parameters.TryGetProperty("properties", out var props)
                        && props.ValueKind == JsonValueKind.Object
                        && props.TryGetProperty("synthesized_text", out _))
                        hasSynthesizeSchema = true;
                }
            }

            if (root.TryGetProperty("tool_choice", out var tc) && tc.ValueKind == JsonValueKind.Object)
            {
                var type = tc.TryGetProperty("type", out var t) ? t.GetString() : null;
                toolChoice = type == "function"
                             && tc.TryGetProperty("function", out var tcf)
                             && tcf.TryGetProperty("name", out var tcn)
                    ? $"function:{tcn.GetString()}"
                    : type;
            }

            documentMode = root.TryGetProperty("document_mode", out var dm) && dm.ValueKind == JsonValueKind.True;
            document = root.TryGetProperty("document", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            contextDocument = root.TryGetProperty("context_document", out var cd) && cd.ValueKind == JsonValueKind.String
                ? cd.GetString()
                : null;
        }

        var system = messages.FirstOrDefault(x => x.Role == "system")?.Content ?? string.Empty;
        var callRole = Classify(toolNames, system, documentMode);

        return new RecordedLlmCall(
            index, method, url, model, callRole, messages, documentMode, document, contextDocument,
            toolNames, toolChoice, hasSynthesizeSchema, body);
    }

    private static LlmRole Classify(IReadOnlyList<string> toolNames, string systemPrompt, bool documentMode)
    {
        if (toolNames.Contains("submit_review"))
            return LlmRole.Reviewer;
        if (toolNames.Contains("submit_refinement"))
            return LlmRole.Refiner;
        if (systemPrompt.Contains("NO_SEARCH", StringComparison.Ordinal))
            return LlmRole.QueryExtractor;
        if (documentMode || systemPrompt.TrimStart().StartsWith(ExecutorPromptPrefix, StringComparison.Ordinal))
            return LlmRole.Executor;
        return LlmRole.Other;
    }
}
