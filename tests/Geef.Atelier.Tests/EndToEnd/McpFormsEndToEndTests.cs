using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The skill and the form of <c>submit_order</c> (D-088) over the production wiring on the real Kestrel server, with the SDK client of 2.4.0 on
/// protocol revision 2026-07-28 and on 2025-11-25. The intake runs for real (worker, clarifier against the faked gateway, gate), and so do the
/// answers (<c>OrderService.AnswerAsync</c>), the run and the database.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed partial class McpFormsEndToEndTests(PostgresFixture postgres)
{
    private const string OlderRevision = "2025-11-25";
    private const string CurrentRevision = "2026-07-28";
    private const string Request = "Schreib eine Einladung zum Sommerfest.";
    private const string OtherToken = "e2e-other-token";
    private const string OtherUser = "e2e-other-user";

    private const string AskLength =
        """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Etwa 150 Wörter","confidence":"unsure","question":null}""";

    private const string AskFormality =
        """{"topic":"formality","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Per Sie","confidence":"unsure","question":{"text":"Sollen die Gäste geduzt oder gesiezt werden?","options":["Geduzt","Gesiezt"],"default_index":null,"allow_free_text":false}}""";

    private const string AssumeTone =
        """{"topic":"tone","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Locker und herzlich","confidence":"sure","question":null}""";

    private static string Clarify(string gaps) =>
        $$"""{"text_type":"Einladung","language":"de","understanding":"Eine Einladung zum Sommerfest des Teams.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{{gaps}}],"research_topics":[]}""";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Hallo", count));

    [Fact]
    public async Task A_client_with_forms_answers_in_the_form_and_no_replay_answers_again_or_creates_a_second_order()
    {
        var port = FreePort();
        await using var host = await StartAsync(port, McpElicitationMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AssumeTone}"));
        host.Script.ExecutorText = Words(150);
        var capture = new StateCapture();
        var forms = new List<ElicitRequestParams>();
        await using var client = await ConnectAsync(port, null, capture, Accepting(forms, ("length", "short")));
        Assert.Equal(CurrentRevision, client.NegotiatedProtocolVersion);

        var submitted = await CallAsync(client, "submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var form = Assert.Single(forms);
        Assert.Equal(OrderQuestionForm.Message, form.Message);
        Assert.Equal(["length", "length_text"], form.RequestedSchema!.Properties.Keys);
        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()));
        Assert.Equal(1, await OrderCountAsync(host));

        var order = await LoadAsync(host, orderId);
        Assert.Equal(IntakeOutcome.Answered, order.IntakeOutcome);
        var answer = Assert.Single(IntakeRecordJson.ParseAnswers(order.AnswersJson));
        Assert.Equal(new OrderAnswer("length", "short", null, false, AnswerOrigin.Form), answer);
        Assert.Matches("\"origin\":\\s*\"Form\"", order.AnswersJson);

        await WaitUntilDoneAsync(host, orderId);
        var prompt = Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains("Kurz (etwa 150 Wörter)", prompt);
        Assert.DoesNotContain("answered by the assistant", prompt);

        var state = Assert.Single(capture.States);
        var replay = await client.CallToolAsync(Replay(new { request = Request }, state, ("length", "long")));
        Assert.NotEqual(true, replay.IsError);
        var again = ToolText(replay);
        Assert.Equal(orderId, again.GetProperty("orderId").GetString());
        Assert.NotEqual("needs_input", again.GetProperty("state").GetString());
        Assert.Equal(answer, Assert.Single(IntakeRecordJson.ParseAnswers((await LoadAsync(host, orderId)).AnswersJson)));

        var changed = await client.CallToolAsync(Replay(new { request = Request + " Bitte kurz." }, state, ("length", "long")));
        Assert.True(changed.IsError);
        Assert.Contains(McpOrderForms.NotMatchedMessage, Text(changed), StringComparison.Ordinal);

        var forged = await client.CallToolAsync(Replay(new { request = Request }, state[..^4] + "AAAA", ("length", "long")));
        Assert.True(forged.IsError);
        Assert.Contains(McpOrderForms.NotMatchedMessage, Text(forged), StringComparison.Ordinal);

        await using var stranger = await ConnectAsync(port, null, null, Accepting([], ("length", "long")), OtherToken);
        var foreign = await stranger.CallToolAsync(Replay(new { request = Request }, state, ("length", "long")));
        Assert.True(foreign.IsError);
        Assert.Contains(McpOrderForms.NotMatchedMessage, Text(foreign), StringComparison.Ordinal);
        Assert.DoesNotContain(orderId, Text(foreign), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, await OrderCountAsync(host));
        Assert.Equal(answer, Assert.Single(IntakeRecordJson.ParseAnswers((await LoadAsync(host, orderId)).AnswersJson)));
    }

    [Fact]
    public async Task A_state_of_another_user_never_answers_their_waiting_order()
    {
        var port = FreePort();
        await using var host = await StartAsync(port, McpElicitationMode.On);
        host.Script.EnqueueClarifierJson(Clarify(AskLength));
        var capture = new StateCapture();
        await using var owner = await ConnectAsync(port, null, capture, (_, _) => ValueTask.FromResult(new ElicitResult { Action = "cancel" }));

        var waiting = await CallAsync(owner, "submit_order", new { request = Request });
        var orderId = waiting.GetProperty("orderId").GetString()!;
        Assert.Equal("needs_input", waiting.GetProperty("state").GetString());

        await using var stranger = await ConnectAsync(port, null, null, null, OtherToken);
        var foreign = await stranger.CallToolAsync(Replay(new { request = Request }, Assert.Single(capture.States), ("length", "long")));

        Assert.True(foreign.IsError);
        Assert.Contains(McpOrderForms.NotMatchedMessage, Text(foreign), StringComparison.Ordinal);
        var order = await LoadAsync(host, orderId);
        Assert.Equal(OrderState.NeedsInput, order.State);
        Assert.Null(order.AnswersJson);
        Assert.Equal(1, await OrderCountAsync(host));
    }

    [Theory]
    [InlineData("cancel", McpOrderForms.CancelledLead)]
    [InlineData("decline", McpOrderForms.DeclinedLead)]
    public async Task Closing_or_declining_the_form_gives_needs_input_and_answer_order_still_works(string action, string lead)
    {
        var port = FreePort();
        await using var host = await StartAsync(port, McpElicitationMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AssumeTone}"));
        host.Script.ExecutorText = Words(150);
        var asked = 0;
        await using var client = await ConnectAsync(port, null, null, (_, _) =>
        {
            asked++;
            return ValueTask.FromResult(new ElicitResult { Action = action });
        });

        var submitted = await CallAsync(client, "submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        Assert.Equal(1, asked);
        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        Assert.StartsWith(lead + " Before writing, Atelier has 1 short question.", submitted.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("length", Assert.Single(submitted.GetProperty("questions").EnumerateArray()).GetProperty("id").GetString());
        var orderId = submitted.GetProperty("orderId").GetString()!;

        var answered = await CallAsync(client, "answer_order", new
        {
            orderId,
            answers = new[] { new { questionId = "length", optionId = "short", origin = "user" } },
        });
        Assert.Contains(answered.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.Equal(AnswerOrigin.User, Assert.Single(IntakeRecordJson.ParseAnswers((await LoadAsync(host, orderId)).AnswersJson)).Origin);
        Assert.Equal(1, await OrderCountAsync(host));
    }

    [Fact]
    public async Task You_decide_and_empty_questions_stay_open_for_the_writer_as_d081_says()
    {
        var port = FreePort();
        await using var host = await StartAsync(port, McpElicitationMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AskFormality}"));
        host.Script.ExecutorText = Words(150);
        var forms = new List<ElicitRequestParams>();
        await using var client = await ConnectAsync(port, null, null, Accepting(forms, ("length", OrderQuestionForm.DecideForMeOption)));

        var submitted = await CallAsync(client, "submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        var keys = Assert.Single(forms).RequestedSchema!.Properties.Keys.ToList();
        Assert.Equal(2, keys.Count(k => !k.EndsWith(OrderQuestionForm.OwnWordsSuffix, StringComparison.Ordinal)));
        Assert.All(IntakeRecordJson.ParseAnswers((await LoadAsync(host, orderId)).AnswersJson), a =>
        {
            Assert.True(a.DecideForMe);
            Assert.Equal(AnswerOrigin.Form, a.Origin);
        });

        await WaitUntilDoneAsync(host, orderId);
        var prompt = Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading, prompt, StringComparison.Ordinal);
        Assert.Contains("Sollen die Gäste geduzt oder gesiezt werden?", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_that_comes_back_too_late_gives_needs_input_and_answers_nothing()
    {
        var port = FreePort();
        await using var host = await StartAsync(port, McpElicitationMode.On, TimeSpan.FromSeconds(1));
        host.Script.EnqueueClarifierJson(Clarify(AskLength));
        await using var client = await ConnectAsync(port, null, null, async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return Answer(("length", "short"));
        });

        var submitted = await CallAsync(client, "submit_order", new { request = Request });

        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        Assert.StartsWith(McpOrderForms.ExpiredLead, submitted.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Null((await LoadAsync(host, submitted.GetProperty("orderId").GetString()!)).AnswersJson);
    }

    [Theory]
    [InlineData(McpElicitationMode.On, OlderRevision, true)]
    [InlineData(McpElicitationMode.On, null, false)]
    [InlineData(McpElicitationMode.Off, null, true)]
    public async Task Older_clients_clients_without_forms_and_the_switch_off_get_needs_input_exactly_as_before(
        McpElicitationMode mode, string? revision, bool withFormHandler)
    {
        var port = FreePort();
        await using var host = await StartAsync(port, mode);
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AssumeTone}"));
        var asked = 0;
        await using var client = await ConnectAsync(port, revision, null, withFormHandler
            ? (_, _) =>
            {
                asked++;
                return ValueTask.FromResult(Answer(("length", "short")));
            }
            : null);

        var submitted = await CallAsync(client, "submit_order", new { request = Request });

        Assert.Equal(0, asked);
        Assert.Equal(revision ?? CurrentRevision, client.NegotiatedProtocolVersion);
        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        Assert.Equal(Geef.Atelier.Mcp.Models.OrderDtoMappings.NeedsInputMessage(1), submitted.GetProperty("message").GetString());
        Assert.Null((await LoadAsync(host, submitted.GetProperty("orderId").GetString()!)).AnswersJson);
    }

    [Fact]
    public async Task With_skills_on_a_current_client_lists_gets_and_reads_the_verified_skill_and_instructions_stay_the_same()
    {
        var port = FreePort();
        string offInstructions;
        IList<McpClientTool> offTools;
        await using (var off = await StartAsync(port, McpElicitationMode.Off))
        await using (var client = await ConnectAsync(port, null, null, null))
        {
            Assert.False(client.ServerCapabilities.SupportsSkills());
            Assert.Null(client.ServerCapabilities.Resources);
            offInstructions = client.ServerInstructions!;
            offTools = await client.ListToolsAsync();
        }

        var onPort = FreePort();
        await using var on = await StartAsync(onPort, McpElicitationMode.Off, skills: McpSkillsMode.On);
        foreach (var revision in new string?[] { null, OlderRevision })
        {
            await using var client = await ConnectAsync(onPort, revision, null, null);
            Assert.NotNull(client.ServerCapabilities.Tools);
            Assert.Equal(offInstructions, client.ServerInstructions);
            var tools = await client.ListToolsAsync();
            Assert.Equal(offTools.Select(t => t.Name).Order(StringComparer.Ordinal), tools.Select(t => t.Name).Order(StringComparer.Ordinal));
            Assert.Equal(offTools.Single(t => t.Name == "submit_order").Description + Geef.Atelier.Mcp.Tools.SubmitOrderTool.SkillSentence,
                tools.Single(t => t.Name == "submit_order").Description);
            if (revision is not null)
                continue;

            Assert.True(client.ServerCapabilities.SupportsSkills());
            var listed = Assert.Single(await client.ListSkillsAsync());
            Assert.Equal(AtelierSkill.Uri, listed.Uri);
            Assert.Equal(AtelierSkill.Name, listed.Name);
            var skill = await client.GetSkillAsync(AtelierSkill.Uri);
            Assert.Equal(4, skill.Resources.Entries!.Count);
            foreach (var entry in skill.Resources.Entries)
            {
                var read = await client.ReadSkillResourceAsync(skill, entry.Uri);
                var text = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents)).Text;
                Assert.Equal(AtelierSkill.Text(entry.Uri[$"skill://{AtelierSkill.Name}/".Length..]), text);
            }
        }
    }

    private Task<AtelierEndToEndHost> StartAsync(int port, McpElicitationMode mode, TimeSpan? lifetime = null, McpSkillsMode skills = McpSkillsMode.Off) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o => o.IntakeMode = IntakeMode.On);
            services.PostConfigure<IntakeOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(200));
            services.PostConfigure<McpOptions>(o =>
            {
                o.Elicitation = mode;
                o.Skills = skills;
                if (lifetime is { } value)
                    o.ElicitationLifetime = value;
            });
            var validator = services.Last(d => d.ServiceType == typeof(ITokenValidator));
            services.Remove(validator);
            services.AddScoped<ITokenValidator>(sp => new SecondUserTokens((ITokenValidator)ActivatorUtilities.CreateInstance(sp, validator.ImplementationType!)));
        }, kestrelPort: port);

    private static Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> Accepting(List<ElicitRequestParams> forms, params (string Key, string Value)[] values) =>
        (form, _) =>
        {
            forms.Add(form!);
            return ValueTask.FromResult(Answer(values));
        };

    private static ElicitResult Answer(params (string Key, string Value)[] values) =>
        new() { Action = "accept", Content = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value)) };

    private static CallToolRequestParams Replay(object arguments, string state, params (string Key, string Value)[] values) => new()
    {
        Name = "submit_order",
        Arguments = JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
        RequestState = state,
        InputResponses = new Dictionary<string, InputResponse>
        {
            [OrderQuestionForm.InputKey] = JsonSerializer.Deserialize<InputResponse>(JsonSerializer.Serialize(Answer(values), McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions)!,
        },
    };

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, object arguments)
    {
        var result = await client.CallToolAsync(tool, JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));
        Assert.True(result.IsError != true, Text(result));
        return ToolText(result);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static JsonElement ToolText(CallToolResult result)
    {
        using var document = JsonDocument.Parse(Text(result));
        return document.RootElement.Clone();
    }

    private static Task<McpClient> ConnectAsync(int port, string? revision, StateCapture? capture,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitation, string token = AtelierEndToEndHost.McpToken)
    {
        var http = new HttpClient(capture is null ? new SocketsHttpHandler() : new CapturingHandler(capture), disposeHandler: true)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromMinutes(3),
        };
        return McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                },
                http, ownsHttpClient: true),
            new McpClientOptions
            {
                ProtocolVersion = revision,
                ClientInfo = new Implementation { Name = "forms-test", Version = "1.0" },
                Capabilities = elicitation is null ? null : new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers { ElicitationHandler = elicitation },
            });
    }

    private static Task<int> OrderCountAsync(AtelierEndToEndHost host) => host.QueryAsync(db => db.Orders.CountAsync());

    private static Task<OrderEntity> LoadAsync(AtelierEndToEndHost host, string orderId)
    {
        var id = Guid.Parse(orderId);
        return host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == id));
    }

    private static async Task WaitUntilDoneAsync(AtelierEndToEndHost host, string orderId)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
            if (status.GetProperty("done").GetBoolean())
                return;
        }

        throw new TimeoutException($"Order {orderId} did not finish.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StateCapture
    {
        public List<string> States { get; } = [];
    }

    private sealed partial class CapturingHandler(StateCapture capture) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Content.Headers.ContentType is not { } type)
                return response;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (Match match in StatePattern().Matches(body))
                lock (capture.States)
                    capture.States.Add(match.Groups[1].Value);
            var replaced = new StringContent(body, Encoding.UTF8);
            replaced.Headers.ContentType = type;
            response.Content = replaced;
            return response;
        }

        [GeneratedRegex("\"requestState\":\"([^\"]+)\"")]
        private static partial Regex StatePattern();
    }

    private sealed class SecondUserTokens(ITokenValidator inner) : ITokenValidator
    {
        public Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken ct = default) =>
            token == OtherToken
                ? Task.FromResult(new TokenValidationOutcome(true, "oauth-bearer", OtherUser, "other-client", null))
                : inner.ValidateTokenAsync(token, ct);
    }
}
