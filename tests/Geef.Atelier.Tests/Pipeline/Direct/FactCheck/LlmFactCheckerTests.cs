using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Infrastructure.Pricing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

public sealed class LlmFactCheckerTests
{
    private static readonly ReviewerProfile Profile = SystemCrew.SourceFactCheckProfile;

    private static readonly TimeSpan Limit = new DirectOptions().FactCheckTimeout;

    private sealed class Resolver(ILlmClient client, bool structured = true) : ILlmClientResolver
    {
        public List<(string Provider, string Model, int? MaxTokens)> Profiles { get; } = [];
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) => throw new NotSupportedException();
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
        {
            Profiles.Add((provider, model, maxTokens));
            return (client, model, maxTokens ?? 0);
        }
        public bool SupportsAgenticTools(string providerName) => false;
        public bool SupportsStructuredOutputs(string providerName) => structured;
    }

    private sealed class Handler(Func<string, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return await answer(body, cancellationToken);
        }
    }

    private sealed record Setup(LlmFactChecker Checker, Handler Handler, Resolver Resolver, RunCostAccumulator Costs);

    private static HttpResponseMessage Completion(string? content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 1200, ["completion_tokens"] = 80, ["total_tokens"] = 1280 },
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string? code) => new(status)
    {
        Content = new StringContent(new JsonObject
        {
            ["error"] = new JsonObject { ["message"] = "scripted", ["type"] = "api_error", ["code"] = code },
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static Setup Create(Func<string, CancellationToken, Task<HttpResponseMessage>> answer, DirectOptions? options = null, bool structured = true)
    {
        var handler = new Handler(answer);
        var client = new OpenAiCompatibleClient(new HttpClient(handler), "http://cli-proxy.test.invalid:8090/v1/codex", "k");
        var resolver = new Resolver(client, structured);
        var costs = new RunCostAccumulator();
        var checker = new LlmFactChecker(resolver, options ?? new DirectOptions(), null, costs, NullLogger<LlmFactChecker>.Instance);
        return new Setup(checker, handler, resolver, costs);
    }

    private static Setup Answering(string? content, DirectOptions? options = null) =>
        Create((_, _) => Task.FromResult(Completion(content)), options);

    private static FactMaterial Material() => FactCheckPromptsTests.Material(FactCheckScope.Full,
        new FactSource("S1", "notizen.txt", "ERSTE-DATEI-ANFANG Bastelmaterial 22 Euro\nSumme 62 Euro ERSTE-DATEI-ENDE"),
        new FactSource("S2", "protokoll.md", "ZWEITE-DATEI-ANFANG\n" + new string('p', 30_000) + "\nZWEITE-DATEI-ENDE"));

    private static readonly FactFinding Previous = new("f1", FactFindingKind.Unsupported, "Fotos liegen diesem Schreiben bei.", "Not backed.",
        null, null, null, null, null, true, QuoteMatchKind.Exact);

    [Fact]
    public async Task The_check_asks_the_profile_model_with_the_schema_and_without_proxy_failover()
    {
        var setup = Answering(FactCheckOutputParserTests.Valid);

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Null(call.ErrorCode);
        Assert.Equal(4, call.Proposed!.Count);
        Assert.Equal(FactCheckOutputParserTests.Valid, call.RawJson);
        Assert.Equal(Profile.Model, call.Model);
        Assert.Equal([(Profile.Provider, Profile.Model, (int?)8_192)], setup.Resolver.Profiles);

        var (request, body) = Assert.Single(setup.Handler.Requests);
        Assert.Equal([OpenAiCompatibleClient.FailoverOff], request.Headers.GetValues(OpenAiCompatibleClient.FailoverHeader));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(Profile.Model, root.GetProperty("model").GetString());
        Assert.Equal(8_192, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.False(root.TryGetProperty("document_mode", out _));
        Assert.False(root.TryGetProperty("tools", out _));
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal(FactCheckSchema.CheckName, format.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(FactCheckSchema.CheckJson), JsonNode.Parse(format.GetProperty("json_schema").GetProperty("schema").GetRawText())));
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(FactCheckPrompts.System, messages[0].GetProperty("content").GetString());
        Assert.Equal(FactCheckPrompts.User(Material(), FactCheckPromptsTests.Draft), messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task The_request_holds_the_first_and_last_words_of_every_file_and_no_assumptions()
    {
        var setup = Answering(FactCheckOutputParserTests.Empty);

        await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(setup.Handler.Requests).Body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        foreach (var marker in new[] { "ERSTE-DATEI-ANFANG", "ERSTE-DATEI-ENDE", "ZWEITE-DATEI-ANFANG", "ZWEITE-DATEI-ENDE" })
            Assert.Contains(marker, body);
        Assert.DoesNotContain(IntakeBriefComposer.AssumptionsHeadingStart, body);
        Assert.DoesNotContain(IntakeBriefComposer.OpenFactsHeadingStart, body);
        Assert.DoesNotContain("Assumptions made before writing", body);
        Assert.DoesNotContain(WriterNotesFormat.StartMarker, body);
    }

    [Fact]
    public async Task Reasoning_effort_and_output_limit_come_from_the_options()
    {
        var setup = Answering(FactCheckOutputParserTests.Empty, new DirectOptions { FactCheckReasoningEffort = "low", FactCheckMaxTokens = 4_000 });

        await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(setup.Handler.Requests).Body);
        Assert.Equal("low", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal(4_000, json.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task The_model_of_another_profile_is_used_as_given()
    {
        var setup = Answering(FactCheckOutputParserTests.Empty);
        var fallback = Profile with { Provider = "claude-cli", Model = "claude-sonnet-latest" };

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, fallback, Limit, 1, CancellationToken.None);

        Assert.Equal("claude-sonnet-latest", call.Model);
        Assert.Equal([("claude-cli", "claude-sonnet-latest", (int?)8_192)], setup.Resolver.Profiles);
        Assert.Equal([OpenAiCompatibleClient.FailoverOff], Assert.Single(setup.Handler.Requests).Request.Headers.GetValues(OpenAiCompatibleClient.FailoverHeader));
    }

    [Fact]
    public async Task An_empty_list_is_a_successful_check_without_findings()
    {
        var setup = Answering(FactCheckOutputParserTests.Empty);

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Null(call.ErrorCode);
        Assert.Empty(call.Proposed!);
    }

    [Fact]
    public async Task Without_structured_outputs_the_forced_tool_is_used()
    {
        var setup = Create((_, _) => Task.FromResult(Completion(FactCheckOutputParserTests.Empty)), structured: false);

        await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(setup.Handler.Requests).Body);
        Assert.False(json.RootElement.TryGetProperty("response_format", out _));
        Assert.Equal(FactCheckSchema.CheckName, json.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_refusal_comes_back_as_null_with_its_code()
    {
        var setup = Answering(null);

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Null(call.Proposed);
        Assert.Null(call.RawJson);
        Assert.Equal(LlmFactChecker.NoJsonCode, call.ErrorCode);
        Assert.Equal(Profile.Model, call.Model);
        Assert.False(LlmFactChecker.IsUnavailable(call.ErrorCode));
    }

    [Fact]
    public async Task Json_without_the_list_comes_back_as_null_with_the_raw_answer()
    {
        var setup = Answering("""{"verdict":"fine"}""");

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Null(call.Proposed);
        Assert.Equal("""{"verdict":"fine"}""", call.RawJson);
        Assert.Equal(LlmFactChecker.UnusableCode, call.ErrorCode);
    }

    [Fact]
    public async Task A_slow_answer_times_out_after_the_limit_without_exception()
    {
        var setup = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Completion(FactCheckOutputParserTests.Empty);
        });

        var watch = Stopwatch.StartNew();
        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, TimeSpan.FromMilliseconds(200), 1, CancellationToken.None);

        Assert.Equal(LlmFactChecker.TimeoutCode, call.ErrorCode);
        Assert.Null(call.Proposed);
        Assert.InRange(call.DurationMs, 150, 10_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(LlmFactChecker.IsUnavailable(call.ErrorCode));
        Assert.Empty(setup.Costs.Flush());
    }

    [Theory]
    [InlineData(90, "85")]
    [InlineData(60, "55")]
    [InlineData(4, "1")]
    public async Task The_gateway_is_asked_to_end_the_cli_process_a_little_before_the_own_limit(int seconds, string header)
    {
        var setup = Answering(FactCheckOutputParserTests.Empty);

        await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, TimeSpan.FromSeconds(seconds), 1, CancellationToken.None);
        await setup.Checker.RecheckAsync(Material(), FactCheckPromptsTests.Draft, [Previous], Profile, TimeSpan.FromSeconds(seconds), CancellationToken.None);

        Assert.All(setup.Handler.Requests, r => Assert.Equal([header], r.Request.Headers.GetValues(OpenAiCompatibleClient.TimeoutHeader)));
    }

    [Fact]
    public void The_shipped_limits_send_85_and_55_seconds()
    {
        var options = new DirectOptions();

        Assert.Equal(85, LlmFactChecker.ProxyTimeoutSeconds(options.FactCheckTimeout));
        Assert.Equal(55, LlmFactChecker.ProxyTimeoutSeconds(options.FactCheckFallbackTimeout));
        Assert.Equal(TimeSpan.FromSeconds(5), LlmFactChecker.ProxyTimeoutMargin);
    }

    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout, LlmApiError.CliTimeoutCode)]
    [InlineData(HttpStatusCode.GatewayTimeout, null)]
    [InlineData(HttpStatusCode.BadGateway, LlmApiError.CliTimeoutCode)]
    public async Task The_gateway_ending_the_call_at_its_limit_counts_as_a_hang(HttpStatusCode status, string? code)
    {
        var setup = Create((_, _) => Task.FromResult(Error(status, code)));

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Equal(LlmFactChecker.TimeoutCode, call.ErrorCode);
        Assert.True(LlmFactChecker.IsUnavailable(call.ErrorCode));
        Assert.Single(setup.Handler.Requests);
    }

    [Fact]
    public async Task A_request_without_a_limit_sends_no_timeout_header()
    {
        var handler = new Handler((_, _) => Task.FromResult(Completion("ok")));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), "http://cli-proxy.test.invalid:8090/v1/claude", "k");

        await client.CompleteAsync(new LlmRequest { Model = "claude-opus-latest", SystemPrompt = "s", UserPrompt = "u", DisableFailover = true }, CancellationToken.None);

        Assert.False(Assert.Single(handler.Requests).Request.Headers.Contains(OpenAiCompatibleClient.TimeoutHeader));
    }

    [Fact]
    public async Task Cancelling_the_caller_throws()
    {
        var setup = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Completion(FactCheckOutputParserTests.Empty);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, LlmApiError.BackendUnavailableCode, LlmApiError.BackendUnavailableCode, true)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmFactChecker.RateLimitCode, LlmFactChecker.RateLimitCode, true)]
    [InlineData(HttpStatusCode.BadGateway, null, "http_502", true)]
    [InlineData(HttpStatusCode.BadRequest, "unsupported_value", "unsupported_value", false)]
    [InlineData(HttpStatusCode.Unauthorized, null, "http_401", false)]
    public async Task An_http_error_comes_back_as_its_code_without_exception(HttpStatusCode status, string? code, string expected, bool unavailable)
    {
        var setup = Create((_, _) => Task.FromResult(Error(status, code)));

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Null(call.Proposed);
        Assert.Equal(expected, call.ErrorCode);
        Assert.Equal(unavailable, LlmFactChecker.IsUnavailable(call.ErrorCode));
        Assert.Single(setup.Handler.Requests);
    }

    [Fact]
    public async Task Any_other_failure_comes_back_as_a_failure_code()
    {
        var setup = Create((_, _) => throw new InvalidOperationException("boom"));

        var call = await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);

        Assert.Equal(LlmFactChecker.FailureCode, call.ErrorCode);
        Assert.False(LlmFactChecker.IsUnavailable(call.ErrorCode));
    }

    [Fact]
    public async Task Costs_of_check_and_recheck_are_booked_on_rounds_one_and_two()
    {
        var answers = new Queue<string>([FactCheckOutputParserTests.Empty, """{"results":[{"id":"f1","status":"fixed","text_quote":null}]}"""]);
        var setup = Create((_, _) => Task.FromResult(Completion(answers.Dequeue())));

        await setup.Checker.CheckAsync(Material(), FactCheckPromptsTests.Draft, Profile, Limit, 1, CancellationToken.None);
        await setup.Checker.RecheckAsync(Material(), FactCheckPromptsTests.Draft, [Previous], Profile, Limit, CancellationToken.None);

        var costs = setup.Costs.Flush().OrderBy(c => c.IterationNumber).ToList();
        Assert.Equal([1, 2], costs.Select(c => c.IterationNumber));
        Assert.All(costs, c =>
        {
            Assert.Equal(ActorType.Reviewer, c.ActorType);
            Assert.Equal(Profile.Name, c.ActorName);
            Assert.Equal(Profile.Model, c.ModelName);
            Assert.Equal(Profile.Provider, c.ProviderName);
            Assert.Equal((1200, 80), (c.InputTokens, c.OutputTokens));
        });
    }

    [Fact]
    public async Task The_recheck_asks_with_its_own_schema_and_prompt()
    {
        const string answer = """{"results":[{"id":"f1","status":"still_there","text_quote":"Fotos liegen diesem Schreiben bei."}]}""";
        var setup = Answering(answer);

        var call = await setup.Checker.RecheckAsync(Material(), FactCheckPromptsTests.Draft, [Previous], Profile, Limit, CancellationToken.None);

        Assert.Null(call.ErrorCode);
        Assert.Equal([new RecheckResult("f1", RecheckStatus.StillThere, "Fotos liegen diesem Schreiben bei.")], call.Results);
        Assert.Equal(answer, call.RawJson);
        var (request, body) = Assert.Single(setup.Handler.Requests);
        Assert.Equal([OpenAiCompatibleClient.FailoverOff], request.Headers.GetValues(OpenAiCompatibleClient.FailoverHeader));
        using var json = JsonDocument.Parse(body);
        Assert.Equal(FactCheckSchema.RecheckName, json.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString());
        var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(FactCheckPrompts.RecheckSystem, messages[0].GetProperty("content").GetString());
        Assert.Equal(FactCheckPrompts.RecheckUser(Material(), FactCheckPromptsTests.Draft, [Previous]), messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_failed_recheck_comes_back_as_its_code_without_exception()
    {
        var setup = Create((_, _) => Task.FromResult(Error(HttpStatusCode.ServiceUnavailable, LlmApiError.BackendUnavailableCode)));

        var call = await setup.Checker.RecheckAsync(Material(), FactCheckPromptsTests.Draft, [Previous], Profile, Limit, CancellationToken.None);

        Assert.Null(call.Results);
        Assert.Equal(LlmApiError.BackendUnavailableCode, call.ErrorCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(LlmFactChecker.TimeoutCode, true)]
    [InlineData(LlmFactChecker.HttpCode, true)]
    [InlineData("http_503", true)]
    [InlineData("http_504", true)]
    [InlineData(LlmFactChecker.NoJsonCode, false)]
    [InlineData(LlmFactChecker.UnusableCode, false)]
    [InlineData(LlmApiError.EmptyResultCode, false)]
    [InlineData("http_400", false)]
    public void Only_unreachable_models_call_for_another_model(string? code, bool expected) =>
        Assert.Equal(expected, LlmFactChecker.IsUnavailable(code));
}
