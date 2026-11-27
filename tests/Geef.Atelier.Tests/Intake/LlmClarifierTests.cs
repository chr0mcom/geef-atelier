using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Infrastructure.Intake;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Intake;

public sealed class LlmClarifierTests
{
    private const string Provider = "claude-cli";
    private const string Model = "claude-sonnet-latest";

    private sealed class Assignments : IModelAssignmentService
    {
        public List<(ModelSlot Slot, ModelDomain Domain)> Calls { get; } = [];

        public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default)
        {
            Calls.Add((slot, domain));
            return Task.FromResult(new ModelAssignment(new ModelCandidate(Provider, Model), false, null));
        }

        public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class Resolver(ILlmClient client, bool structured = true) : ILlmClientResolver
    {
        public List<(string Provider, string Model, int? MaxTokens)> Profiles { get; } = [];
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
        {
            Profiles.Add((provider, model, maxTokens));
            return (client, model, maxTokens ?? 0);
        }
        public bool SupportsStructuredOutputs(string providerName) => structured;
    }

    private sealed class Handler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return await answer(request, body, cancellationToken);
        }
    }

    private static HttpResponseMessage Completion(string? content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 100, ["completion_tokens"] = 50, ["total_tokens"] = 150 },
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static readonly ClarifierRequest Request = new("Schreib eine Einladung zum Sommerfest.", [], null, false);

    private static (LlmClarifier Clarifier, Handler Handler, Assignments Assignments, Resolver Resolver) Create(
        Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> answer, IntakeOptions? options = null, bool structured = true)
    {
        var handler = new Handler(answer);
        var client = new OpenAiCompatibleClient(new HttpClient(handler), "http://cli-proxy.test.invalid:8090/v1/claude", "k");
        var assignments = new Assignments();
        var resolver = new Resolver(client, structured);
        var clarifier = new LlmClarifier(assignments, resolver, Options.Create(options ?? new IntakeOptions()),
            NullLogger<LlmClarifier>.Instance, TimeProvider.System);
        return (clarifier, handler, assignments, resolver);
    }

    [Fact]
    public async Task The_request_uses_the_intake_slot_the_schema_and_low_effort_without_failover_header()
    {
        var (clarifier, handler, assignments, resolver) = Create((_, _, _) => Task.FromResult(Completion(ClarifierOutputParserTests.Valid)));

        var result = await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal(IntakeRecordStatus.Ok, result.Status);
        Assert.Equal(Model, result.Model);
        Assert.Null(result.ErrorCode);
        Assert.Equal("Einladung", result.Output!.TextType);
        Assert.Equal(ClarifierOutputParserTests.Valid, result.RawJson);
        Assert.Equal([(ModelSlot.Intake, ModelDomain.General)], assignments.Calls);
        Assert.Equal([(Provider, Model, (int?)8_192)], resolver.Profiles);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains(OpenAiCompatibleClient.FailoverHeader));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(Model, root.GetProperty("model").GetString());
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(8_192, root.GetProperty("max_tokens").GetInt32());
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal(ClarifierSchema.Name, format.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("tools", out _));
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(ClarifierPrompts.System, messages[0].GetProperty("content").GetString());
        Assert.StartsWith("<request>\nSchreib eine Einladung", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Without_structured_outputs_the_forced_tool_is_used()
    {
        var (clarifier, handler, _, _) = Create((_, _, _) => Task.FromResult(Completion(ClarifierOutputParserTests.Valid)), structured: false);

        await clarifier.ClarifyAsync(Request, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(json.RootElement.TryGetProperty("response_format", out _));
        Assert.Equal(ClarifierSchema.Name, json.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Failover_can_be_switched_off_by_configuration()
    {
        var (clarifier, handler, _, _) = Create((_, _, _) => Task.FromResult(Completion(ClarifierOutputParserTests.Valid)),
            new IntakeOptions { AllowFailover = false });

        await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal([OpenAiCompatibleClient.FailoverSameModel], Assert.Single(handler.Requests).Request.Headers.GetValues(OpenAiCompatibleClient.FailoverHeader));
    }

    [Fact]
    public async Task A_slow_answer_times_out_after_the_limit_without_exception()
    {
        var (clarifier, _, _, _) = Create(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Completion(ClarifierOutputParserTests.Valid);
        }, new IntakeOptions { Timeout = TimeSpan.FromMilliseconds(200) });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal(IntakeRecordStatus.TimedOut, result.Status);
        Assert.Equal(LlmClarifier.TimeoutCode, result.ErrorCode);
        Assert.Null(result.Output);
        Assert.InRange(result.DurationMs, 150, 10_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancelling_the_caller_throws()
    {
        var (clarifier, _, _, _) = Create(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Completion(ClarifierOutputParserTests.Valid);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clarifier.ClarifyAsync(Request, cts.Token));
    }

    [Fact]
    public async Task A_refusal_without_content_is_invalid()
    {
        var (clarifier, _, _, _) = Create((_, _, _) => Task.FromResult(Completion(null)));

        var result = await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal((IntakeRecordStatus.Invalid, LlmClarifier.NoJsonCode), (result.Status, result.ErrorCode));
        Assert.Equal(Model, result.Model);
    }

    [Fact]
    public async Task Json_without_the_required_fields_is_invalid()
    {
        var (clarifier, _, _, _) = Create((_, _, _) => Task.FromResult(Completion("""{"text_type":"x"}""")));

        var result = await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal((IntakeRecordStatus.Invalid, LlmClarifier.UnusableCode), (result.Status, result.ErrorCode));
    }

    [Fact]
    public async Task An_http_error_is_a_failure_with_its_status()
    {
        var (clarifier, _, _, _) = Create((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"bad","code":"bad_request"}}""", Encoding.UTF8, "application/json"),
        }));

        var result = await clarifier.ClarifyAsync(Request, CancellationToken.None);

        Assert.Equal((IntakeRecordStatus.Failed, "http_400"), (result.Status, result.ErrorCode));
    }
}
