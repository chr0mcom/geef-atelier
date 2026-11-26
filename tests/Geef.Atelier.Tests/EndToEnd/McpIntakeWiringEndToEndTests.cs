using System.Diagnostics;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The intake over <c>/mcp</c> with the real wiring and without any stand-in for the order service: <c>submit_order</c> wakes the
/// real <c>OrderIntakeWorker</c>, the real <c>LlmClarifier</c> calls the faked gateway (<see cref="ScriptedLlmEndpoint"/>), the gate
/// decides, and <c>answer_order</c> goes through the real <c>OrderService.AnswerAsync</c> and <c>OrderRunStarter</c>.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpIntakeWiringEndToEndTests(PostgresFixture postgres)
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.";
    private const string AnswerBlock = "Answers the person gave before writing:";

    private const string AskLength =
        """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Etwa 150 Wörter","confidence":"unsure","question":null}""";

    private const string AssumeTone =
        """{"topic":"tone","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Locker und herzlich","confidence":"sure","question":null}""";

    private const string AssumeDate =
        """{"topic":"date","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Datum und Ort als Platzhalter","confidence":"unsure","question":null}""";

    private const string DerivedAudience =
        """{"topic":"audience","resolution":"derived","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"","confidence":"sure","question":null}""";

    private static string Clarify(string gaps) =>
        $$"""{"text_type":"Einladung","language":"de","understanding":"Eine Einladung zum Sommerfest des Teams.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{{gaps}}],"research_topics":[]}""";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Hallo", count));

    [Fact]
    public async Task Questions_come_back_from_the_real_worker_and_answer_order_starts_the_run_with_the_answers()
    {
        await using var host = await StartAsync(IntakeMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AssumeTone},{AssumeDate}"));
        host.Script.ExecutorText = Words(150);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        Assert.True(IsNullOrMissing(submitted, "runId"), submitted.ToString());
        Assert.Equal("de", submitted.GetProperty("language").GetString());
        Assert.Equal("Eine Einladung zum Sommerfest des Teams.", submitted.GetProperty("understanding").GetString());
        var question = Assert.Single(submitted.GetProperty("questions").EnumerateArray());
        Assert.Equal((QuestionTemplates.LengthQuestionId, QuestionTemplates.GermanLengthQuestion),
            (question.GetProperty("id").GetString(), question.GetProperty("text").GetString()));
        Assert.Contains("short", question.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()));
        var assumptions = submitted.GetProperty("assumptions").EnumerateArray().ToDictionary(a => a.GetProperty("text").GetString()!, a => a.GetProperty("id").GetString()!);
        Assert.Contains("Locker und herzlich", assumptions.Keys);
        Assert.Contains("Datum und Ort als Platzhalter", assumptions.Keys);

        var status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
        Assert.Equal("needs_input", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("nextCheckSeconds").GetInt32());

        var unknown = (await host.PostMcpAsync("tools/call", new
        {
            name = "answer_order",
            arguments = new { orderId, answers = new[] { new { questionId = "q9", optionId = "o1", origin = "user" } } },
        })).GetProperty("result");
        Assert.True(unknown.GetProperty("isError").GetBoolean(), unknown.ToString());
        Assert.Equal("An error occurred invoking 'answer_order': There is no question \"q9\" for this order.", unknown.GetProperty("content")[0].GetProperty("text").GetString());

        var dateId = assumptions["Datum und Ort als Platzhalter"];
        var answered = await host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new object[]
            {
                new { questionId = "length", optionId = "short", origin = "user" },
                new { questionId = dateId, freeText = "Freitag, 12. Juli, im Innenhof", origin = "client" },
            },
        });
        Assert.Contains(answered.GetProperty("state").GetString(), new[] { "queued", "working" });
        var runId = answered.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrEmpty(runId));

        await WaitUntilDoneAsync(host, orderId);
        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("text").GetString()), result.ToString());
        var left = Assert.Single(result.GetProperty("assumptions").EnumerateArray());
        Assert.Equal("Locker und herzlich", left.GetProperty("text").GetString());

        Assert.Single(host.Llm.Calls, ScriptedLlmEndpoint.IsClarifierCall);
        var prompt = Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains(AnswerBlock, prompt);
        Assert.Contains("Kurz (etwa 150 Wörter)", prompt);
        Assert.Contains("Freitag, 12. Juli, im Innenhof (answered by the assistant)", prompt);
        var order = await LoadAsync(host, orderId);
        Assert.Equal(IntakeOutcome.Answered, order.IntakeOutcome);

        var repeated = await host.CallMcpToolAsync("answer_order", new { orderId, skip = true });
        Assert.Contains("already started", repeated.GetProperty("message").GetString());
        Assert.Equal(runId, repeated.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task Without_questions_submit_order_starts_at_once_with_understanding_and_assumptions()
    {
        await using var host = await StartAsync(IntakeMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{DerivedAudience},{AssumeTone}"));
        host.Script.ExecutorText = Words(150);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request + " Für das ganze Team, etwa 150 Wörter." });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()));
        Assert.True(IsNullOrMissing(submitted, "questions"), submitted.ToString());
        Assert.Equal("Eine Einladung zum Sommerfest des Teams.", submitted.GetProperty("understanding").GetString());
        Assert.Equal("Locker und herzlich", Assert.Single(submitted.GetProperty("assumptions").EnumerateArray()).GetProperty("text").GetString());

        await WaitUntilDoneAsync(host, orderId);
        var order = await LoadAsync(host, orderId);
        Assert.Equal(IntakeOutcome.Clarified, order.IntakeOutcome);
        Assert.Null(order.QuestionsAskedAt);
    }

    [Fact]
    public async Task A_clarifier_error_starts_the_order_with_the_request_and_a_warning()
    {
        var timeout = TimeSpan.FromSeconds(10);
        await using var host = await StartAsync(IntakeMode.On, timeout);
        host.Script.EnqueueClarifierError(503);
        host.Script.EnqueueClarifierError(503);
        host.Script.EnqueueClarifierError(503);
        host.Script.ExecutorText = Words(150);

        var watch = Stopwatch.StartNew();
        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        watch.Stop();
        host.ReleaseOrchestrator();

        await AssertStartedWithTheRequestAsync(host, submitted, watch.Elapsed, timeout, IntakeRecordStatus.Failed);
    }

    [Fact]
    public async Task A_clarifier_slower_than_the_limit_starts_the_order_with_the_request_and_a_warning()
    {
        var timeout = TimeSpan.FromSeconds(2);
        await using var host = await StartAsync(IntakeMode.On, timeout);
        host.Script.EnqueueClarifierDelay(TimeSpan.FromSeconds(30));
        host.Script.ExecutorText = Words(150);

        var watch = Stopwatch.StartNew();
        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        watch.Stop();
        host.ReleaseOrchestrator();

        await AssertStartedWithTheRequestAsync(host, submitted, watch.Elapsed, timeout, IntakeRecordStatus.TimedOut);
    }

    [Fact]
    public async Task Shadow_mode_starts_the_run_at_once_and_only_writes_the_shadow_row()
    {
        await using var host = await StartAsync(IntakeMode.Shadow);
        host.Script.EnqueueClarifierJson(Clarify(AskLength));
        host.Script.ExecutorText = Words(150);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()));
        Assert.True(IsNullOrMissing(submitted, "questions"), submitted.ToString());
        await WaitUntilDoneAsync(host, orderId);

        var id = Guid.Parse(orderId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        OrderIntakeShadowEntity? shadow = null;
        while (DateTimeOffset.UtcNow < deadline && shadow?.Status != IntakeShadowStatuses.Done)
        {
            shadow = await host.QueryAsync(db => db.OrderIntakeShadows.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == id));
            await Task.Delay(100);
        }
        Assert.Equal(IntakeShadowStatuses.Done, shadow?.Status);
        Assert.Equal(1, shadow!.QuestionCount);

        var order = await LoadAsync(host, orderId);
        Assert.Null(order.ClarifyJson);
        Assert.Null(order.IntakeOutcome);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == id));
        Assert.Equal(Request, run.BriefingText);
    }

    private static async Task AssertStartedWithTheRequestAsync(
        AtelierEndToEndHost host, JsonElement submitted, TimeSpan elapsed, TimeSpan timeout, IntakeRecordStatus status)
    {
        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.True(elapsed < timeout + TimeSpan.FromSeconds(5), $"submit_order answered after {elapsed}.");
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()), submitted.ToString());
        Assert.True(IsNullOrMissing(submitted, "questions"), submitted.ToString());

        await WaitUntilDoneAsync(host, orderId);
        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.Contains(RunWarningCodes.IntakeFailed, result.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()));

        var order = await LoadAsync(host, orderId);
        Assert.Equal(IntakeOutcome.Failed, order.IntakeOutcome);
        Assert.Equal(status, IntakeRecordJson.TryParse(order.ClarifyJson)!.Status);
        var id = Guid.Parse(orderId);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == id));
        Assert.Equal(Request + Geef.Atelier.Application.Orders.BriefingSections.Separator + Geef.Atelier.Application.Orders.IntakeBriefComposer.NotCheckedHeading, run.BriefingText);
        Assert.DoesNotContain(AnswerBlock, Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content);
    }

    private Task<AtelierEndToEndHost> StartAsync(IntakeMode mode, TimeSpan? timeout = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o => o.IntakeMode = mode);
            services.PostConfigure<IntakeOptions>(o =>
            {
                o.PollInterval = TimeSpan.FromMilliseconds(200);
                if (timeout is { } limit)
                    o.Timeout = limit;
            });
        });

    private static Task<OrderEntity> LoadAsync(AtelierEndToEndHost host, string orderId)
    {
        var id = Guid.Parse(orderId);
        return host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == id));
    }

    private static async Task WaitUntilDoneAsync(AtelierEndToEndHost host, string orderId)
    {
        JsonElement status = default;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
            if (status.GetProperty("done").GetBoolean())
                return;
        }

        throw new TimeoutException($"Order not done: {status}\n{host.Llm.Describe()}");
    }

    private static bool IsNullOrMissing(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;
}
