using System.Diagnostics;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The intake over the real wiring (contract stage 2, Q2 acceptance): submit with <c>IntakeMode=On</c>, the worker claims the
/// order, the scripted clarifier answers through the faked gateway, the gate decides, and the run starts or the order waits for
/// answers. Needs the persistence and order service of Q1.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class IntakeEndToEndTests(PostgresFixture postgres)
{
    private const string AnswerBlock = "Answers the person gave before writing:";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Hallo", count));

    private static string Clarify(string gaps = "", bool revision = false, string research = "") =>
        $$"""{"text_type":"Einladung","language":"de","understanding":"Eine Einladung zum Sommerfest des Teams.","is_revision":{{(revision ? "true" : "false")}},"revision_file":null,"length_scale":"standard","gaps":[{{gaps}}],"research_topics":[{{research}}]}""";

    private const string AskLength =
        """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Etwa 150 Wörter","confidence":"unsure","question":null}""";

    private const string AssumeTone =
        """{"topic":"tone","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Locker und herzlich","confidence":"sure","question":null}""";

    private const string DerivedAudience =
        """{"topic":"audience","resolution":"derived","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"","confidence":"sure","question":null}""";

    private const string AskResearchable =
        """{"topic":"facts","resolution":"ask","user_can_know":true,"web_researchable":true,"impact":"high","assumption":"Gegründet vor einigen Jahren","confidence":"unsure","question":{"text":"Wann wurde die Firma gegründet?","options":["Vor 1990","Nach 1990"]}}""";

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, IntakeMode mode, TimeSpan? timeout = null) =>
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

    private static async Task<OrderView> WaitForAsync(AtelierEndToEndHost host, Guid orderId, Func<OrderView, bool> done, List<OrderState>? seen = null, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        OrderView? view = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            view = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(orderId, null));
            if (view is not null)
            {
                seen?.Add(view.State);
                if (done(view))
                    return view;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Order {orderId} stayed in {view?.State.ToString() ?? "missing"}.\n{host.Llm.Describe()}");
    }

    private static IReadOnlyList<RecordedLlmCall> ClarifierCalls(AtelierEndToEndHost host) =>
        host.Llm.Calls.Where(ScriptedLlmEndpoint.IsClarifierCall).ToList();

    [Fact]
    public async Task A_sparse_order_asks_the_length_and_the_answer_becomes_the_brief_and_the_length_check()
    {
        await using var host = await StartAsync(postgres, IntakeMode.On);
        host.Script.EnqueueClarifierJson(Clarify(AskLength));
        host.Script.ExecutorText = Words(150);

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Schreib eine Einladung zum Sommerfest.", "e2e-intake", OrderEntry.Web));
        Assert.Equal(OrderDisplayState.Preparing, submission.State);
        Assert.Null(submission.RunId);

        var waiting = await WaitForAsync(host, submission.OrderId, v => v.State == OrderState.NeedsInput);
        var question = Assert.Single(waiting.Clarification!.Questions);
        Assert.Equal((QuestionTemplates.LengthQuestionId, QuestionTemplates.GermanLengthQuestion), (question.Id, question.Text));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(
            submission.OrderId, "e2e-intake", [new OrderAnswer("length", "short", null, false, AnswerOrigin.User)])));
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        Assert.Single(ClarifierCalls(host));
        var prompt = Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content!;
        Assert.Contains(AnswerBlock, prompt);
        Assert.Contains("Kurz (etwa 150 Wörter)", prompt);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == view.CurrentRun!.RunId));
        var length = Assert.Single(RunReportJson.TryParse(run.ReportJson)!.Checks, c => c.Code == DraftCheckCodes.Length);
        Assert.EndsWith("→ Kurz (etwa 150 Wörter)", length.Quote);
        Assert.Equal((IntakeOutcome?)IntakeOutcome.Answered, view.Clarification!.Outcome);
    }

    [Fact]
    public async Task A_complete_order_starts_without_stopping()
    {
        await using var host = await StartAsync(postgres, IntakeMode.On);
        host.Script.EnqueueClarifierJson(Clarify($"{DerivedAudience},{AssumeTone}"));
        host.Script.ExecutorText = Words(150);
        var seen = new List<OrderState>();

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(
            "Schreib eine Einladung zum Sommerfest am 12. Juli für das ganze Team, etwa 150 Wörter, mit Grillbuffet.", "e2e-intake", OrderEntry.Web));
        await WaitForAsync(host, submission.OrderId, v => v.State == OrderState.Ready, seen);
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        Assert.DoesNotContain(OrderState.NeedsInput, seen);
        Assert.Equal((IntakeOutcome?)IntakeOutcome.Clarified, view.Clarification!.Outcome);
        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        Assert.Null(order.QuestionsAskedAt);
        Assert.DoesNotContain(AnswerBlock, Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content);
    }

    [Fact]
    public async Task A_researchable_gap_is_not_asked_and_its_topic_is_stored()
    {
        await using var host = await StartAsync(postgres, IntakeMode.On);
        host.Script.EnqueueClarifierJson(Clarify(AskResearchable));
        host.Script.ExecutorText = Words(150);

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(
            "Schreib einen Jubiläumspost für die Muster GmbH, etwa 150 Wörter.", "e2e-intake", OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        Assert.NotNull(record);
        Assert.Empty(record.Questions);
        Assert.Equal(GateDecision.ToResearch, Assert.Single(record.Trace).Decision);
        Assert.NotEmpty(record.ResearchTopics);
    }

    [Fact]
    public async Task A_gateway_error_starts_the_order_with_the_request_and_a_warning()
    {
        var timeout = TimeSpan.FromSeconds(10);
        await using var host = await StartAsync(postgres, IntakeMode.On, timeout);
        host.Script.EnqueueClarifierError(503);
        host.Script.EnqueueClarifierError(503);
        host.Script.EnqueueClarifierError(503);
        host.Script.ExecutorText = Words(150);

        var watch = Stopwatch.StartNew();
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Schreib eine Einladung zum Sommerfest.", "e2e-intake", OrderEntry.Web));
        await WaitForAsync(host, submission.OrderId, v => v.State == OrderState.Ready);
        Assert.True(watch.Elapsed < timeout + TimeSpan.FromSeconds(5), $"Started after {watch.Elapsed}.");

        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);
        Assert.Equal((IntakeOutcome?)IntakeOutcome.Failed, view.Clarification?.Outcome);
        Assert.Contains(view.Warnings, w => w.Code == RunWarningCodes.IntakeFailed);
        Assert.DoesNotContain(AnswerBlock, host.Llm.CallsFor(LlmRole.Executor)[0].Messages.Last(m => m.Role == "user").Content);
    }

    [Fact]
    public async Task A_clarifier_slower_than_the_limit_starts_the_order_like_an_error()
    {
        var timeout = TimeSpan.FromSeconds(2);
        await using var host = await StartAsync(postgres, IntakeMode.On, timeout);
        host.Script.EnqueueClarifierDelay(TimeSpan.FromSeconds(30));
        host.Script.ExecutorText = Words(150);

        var watch = Stopwatch.StartNew();
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Schreib eine Einladung zum Sommerfest.", "e2e-intake", OrderEntry.Web));
        await WaitForAsync(host, submission.OrderId, v => v.State == OrderState.Ready);
        Assert.True(watch.Elapsed < timeout + TimeSpan.FromSeconds(5), $"Started after {watch.Elapsed}.");

        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);
        Assert.Contains(view.Warnings, w => w.Code == RunWarningCodes.IntakeFailed);
        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        Assert.Equal(IntakeRecordStatus.TimedOut, IntakeRecordJson.TryParse(order.ClarifyJson)!.Status);
    }

    [Fact]
    public async Task Shadow_mode_starts_at_once_and_only_records_the_shadow_row()
    {
        await using var host = await StartAsync(postgres, IntakeMode.Shadow);
        host.Script.EnqueueClarifierJson(Clarify(AskLength));
        host.Script.ExecutorText = Words(150);

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Schreib eine Einladung zum Sommerfest.", "e2e-intake", OrderEntry.Web));
        Assert.NotNull(submission.RunId);
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        OrderIntakeShadowEntity? shadow = null;
        while (DateTimeOffset.UtcNow < deadline && shadow?.Status != IntakeShadowStatuses.Done)
        {
            shadow = await host.QueryAsync(db => db.OrderIntakeShadows.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == submission.OrderId));
            await Task.Delay(100);
        }
        Assert.Equal(IntakeShadowStatuses.Done, shadow?.Status);
        Assert.Equal(1, shadow!.QuestionCount);

        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        Assert.Null(order.ClarifyJson);
        Assert.Null(order.IntakeOutcome);
        Assert.DoesNotContain(AnswerBlock, Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Messages.Last(m => m.Role == "user").Content);
    }

}
