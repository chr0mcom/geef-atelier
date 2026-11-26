using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The intake over the real <c>/mcp</c> endpoint: <c>submit_order</c> → <c>needs_input</c> → <c>answer_order</c> →
/// <c>get_order_status</c> → <c>get_order_result</c>. The analysis itself is simulated by <see cref="SimulatedIntakeOrderService"/>
/// around the real order service; writing, the database, the MCP transport, the tool binding and the typed answer list are real.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpIntakeEndToEndTests(PostgresFixture postgres)
{
    private const string Request = "Schreib eine kurze Mail an den Kunden: Die Lieferung verzögert sich um zwei Wochen.";

    private static readonly ClarifyQuestion LengthQuestion = new(
        "length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
        [new QuestionOption("short", "Kurz (etwa 150 Wörter)"), new QuestionOption("medium", "Mittel (etwa 400 Wörter)"), new QuestionOption("long", "Lang (etwa 800 Wörter)")],
        null, true, true);

    private static readonly ClarifyQuestion AudienceQuestion = new(
        "q1", GapTopic.Audience, "Wer bekommt die Mail?",
        [new QuestionOption("o1", "Ein einzelner Kunde"), new QuestionOption("o2", "Alle Kunden")],
        "o1", false, false);

    private static readonly IReadOnlyList<IntakeAssumption> Assumptions =
    [
        new("a1", GapTopic.Tone, "Freundlicher, sachlicher Ton", AssumptionConfidence.Sure),
        new("a2", GapTopic.Date, "Neuer Liefertermin als Platzhalter", AssumptionConfidence.Unsure),
    ];

    [Fact]
    public async Task Questions_are_answered_over_mcp_and_the_order_is_written_with_its_assumptions()
    {
        var intake = new SimulatedIntake(Clarification([LengthQuestion, AudienceQuestion]), TimeSpan.FromMilliseconds(300));
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services => intake.Install(services));

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        Assert.True(IsNullOrMissing(submitted, "runId"), submitted.ToString());
        Assert.Equal("de", submitted.GetProperty("language").GetString());
        Assert.Equal("Eine kurze Mail an einen Kunden über eine Lieferverzögerung.", submitted.GetProperty("understanding").GetString());
        var assumptions = submitted.GetProperty("assumptions").EnumerateArray().ToList();
        Assert.Equal(["a1", "a2"], assumptions.Select(a => a.GetProperty("id").GetString()));
        Assert.True(assumptions[1].GetProperty("unsure").GetBoolean());
        var questions = submitted.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(["length", "q1"], questions.Select(q => q.GetProperty("id").GetString()));
        Assert.Equal(["length", "audience"], questions.Select(q => q.GetProperty("topic").GetString()));
        Assert.Equal(["short", "medium", "long"], questions[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()));
        Assert.Equal("Kurz (etwa 150 Wörter)", questions[0].GetProperty("options")[0].GetProperty("label").GetString());
        Assert.True(questions[0].GetProperty("allowFreeText").GetBoolean());
        Assert.True(!questions[1].TryGetProperty("defaultOptionId", out var preselected) || preselected.ValueKind == JsonValueKind.Null, questions[1].ToString());
        Assert.StartsWith("Before writing, Atelier has 2 short questions.", submitted.GetProperty("message").GetString());

        var watch = Stopwatch.StartNew();
        var status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The status call waited {watch.Elapsed} although the order waits for answers.");
        Assert.Equal("needs_input", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("done").GetBoolean());
        Assert.Equal(0, status.GetProperty("nextCheckSeconds").GetInt32());
        Assert.Equal(2, status.GetProperty("questions").GetArrayLength());
        Assert.Contains("answer_order", status.GetProperty("message").GetString());

        var memory = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new[] { new { questionId = "length", optionId = "short", origin = "memory" } },
        }));
        Assert.Contains("cannot be used here", memory.Message);

        var both = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new[] { new { questionId = "length", optionId = "short", origin = "user" } },
            skip = true,
        }));
        Assert.Contains(AnswerOrderTool.AnswerOrSkipMessage, both.Message);

        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new[] { new { questionId = "q9", optionId = "o1", origin = "user" } },
        }));
        Assert.Contains("There is no question q9 in this order.", unknown.Message);
        Assert.Empty(intake.AnswersFor(orderId));

        var answered = await host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new object[]
            {
                new { questionId = "length", optionId = "short", origin = "user" },
                new { questionId = "a2", freeText = "Neuer Termin: Kalenderwoche 12", origin = "client" },
                new { questionId = "q1", decideForMe = true, origin = "user" },
            },
        });

        Assert.Equal(orderId, answered.GetProperty("orderId").GetString());
        Assert.Contains(answered.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(answered.GetProperty("runId").GetString()));
        Assert.StartsWith("Writing has started.", answered.GetProperty("message").GetString());
        Assert.Equal(
            new[]
            {
                new OrderAnswer("length", "short", null, false, AnswerOrigin.User),
                new OrderAnswer("a2", null, "Neuer Termin: Kalenderwoche 12", false, AnswerOrigin.Client),
                new OrderAnswer("q1", null, null, true, AnswerOrigin.User),
            },
            intake.AnswersFor(orderId));
        Assert.Equal(IntakeOutcome.Answered, intake.OutcomeFor(orderId));

        status = await WaitUntilDoneAsync(host, orderId);
        Assert.Contains(status.GetProperty("state").GetString(), new[] { "delivered", "delivered_with_open_points" });
        Assert.True(IsNullOrMissing(status, "questions"), status.ToString());

        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("text").GetString()), result.ToString());
        Assert.Equal(["a1", "a2"], result.GetProperty("assumptions").EnumerateArray().Select(a => a.GetProperty("id").GetString()));

        var repeated = await host.CallMcpToolAsync("answer_order", new { orderId, skip = true });
        Assert.Contains("already started", repeated.GetProperty("message").GetString());
        Assert.Equal(answered.GetProperty("runId").GetString(), repeated.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task A_slow_analysis_answers_with_preparing_and_the_status_tool_brings_the_questions_then_skip_starts()
    {
        var intake = new SimulatedIntake(Clarification([LengthQuestion]), TimeSpan.FromSeconds(4));
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            intake.Install(services);
            services.Configure<IntakeOptions>(o => o.SubmitWaitSeconds = 1);
        });

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Equal("preparing", submitted.GetProperty("state").GetString());
        Assert.True(IsNullOrMissing(submitted, "questions"), submitted.ToString());
        Assert.Equal("Still reading the request. Call get_order_status with waitSeconds=25.", submitted.GetProperty("message").GetString());

        var status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 25 });
        Assert.Equal("needs_input", status.GetProperty("state").GetString());
        Assert.Equal("length", Assert.Single(status.GetProperty("questions").EnumerateArray()).GetProperty("id").GetString());
        Assert.StartsWith("Before writing, Atelier has 1 short question.", status.GetProperty("message").GetString());

        var skipped = await host.CallMcpToolAsync("answer_order", new { orderId, skip = true });
        Assert.Contains(skipped.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.Equal(IntakeOutcome.Skipped, intake.OutcomeFor(orderId));
        Assert.Empty(intake.AnswersFor(orderId));

        await WaitUntilDoneAsync(host, orderId);
        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("text").GetString()), result.ToString());
    }

    [Fact]
    public async Task Without_questions_submit_order_answers_with_the_started_run_understanding_and_assumptions()
    {
        var intake = new SimulatedIntake(Clarification([]), TimeSpan.FromMilliseconds(200));
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services => intake.Install(services));

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()));
        Assert.True(IsNullOrMissing(submitted, "questions"), submitted.ToString());
        Assert.Equal("Eine kurze Mail an einen Kunden über eine Lieferverzögerung.", submitted.GetProperty("understanding").GetString());
        Assert.Equal(2, submitted.GetProperty("assumptions").GetArrayLength());
        Assert.Equal(IntakeOutcome.Clarified, intake.OutcomeFor(orderId));

        await WaitUntilDoneAsync(host, orderId);
    }

    private static ClarificationView Clarification(IReadOnlyList<ClarifyQuestion> questions) =>
        new(IntakeOutcome.Asked, "email", "de", "Eine kurze Mail an einen Kunden über eine Lieferverzögerung.",
            Assumptions, questions, [], null, null, null);

    private static async Task<JsonElement> WaitUntilDoneAsync(AtelierEndToEndHost host, string orderId)
    {
        JsonElement status = default;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
            if (status.GetProperty("done").GetBoolean())
                return status;
        }

        throw new TimeoutException($"Order not done: {status}\n{host.Llm.Describe()}");
    }

    private static bool IsNullOrMissing(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    /// <summary>State of the simulated intake, shared across the DI scopes of one host.</summary>
    private sealed class SimulatedIntake(ClarificationView clarification, TimeSpan analysisTime)
    {
        public const string AlreadyStartedNotice = "This order has already started; the answers were not changed.";

        public ConcurrentDictionary<Guid, PendingOrder> Orders { get; } = new();

        public ClarificationView Clarification { get; } = clarification;

        public TimeSpan AnalysisTime { get; } = analysisTime;

        public IReadOnlyList<OrderAnswer> AnswersFor(string orderId) => Orders[Guid.Parse(orderId)].Answers;

        public IntakeOutcome? OutcomeFor(string orderId) => Orders[Guid.Parse(orderId)].Outcome;

        public void Install(IServiceCollection services)
        {
            var original = services.Single(d => d.ServiceType == typeof(IOrderService));
            services.Remove(original);
            services.AddScoped<IOrderService>(sp => new SimulatedIntakeOrderService(
                (IOrderService)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), this));
        }
    }

    private sealed class PendingOrder(SubmitOrderRequest request, DateTimeOffset createdAt)
    {
        public SubmitOrderRequest Request { get; } = request;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public OrderDisplayState State { get; set; } = OrderDisplayState.Preparing;
        public Guid? RealOrderId { get; set; }
        public Guid? RunId { get; set; }
        public IntakeOutcome? Outcome { get; set; }
        public IReadOnlyList<OrderAnswer> Answers { get; set; } = [];
    }

    /// <summary>
    /// Stands in for the intake of stage 2 (worker, clarifier, <c>AnswerAsync</c>) until the real one is merged: orders that may ask
    /// questions wait in <c>Preparing</c> for <see cref="SimulatedIntake.AnalysisTime"/>, then show the scripted clarification. Starting
    /// one submits the request to the real order service (stage 1 path), so the run, the writing and the result are real.
    /// </summary>
    private sealed class SimulatedIntakeOrderService(IOrderService inner, SimulatedIntake intake) : IOrderService
    {
        public async Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default)
        {
            if (!request.AllowQuestions)
                return await inner.SubmitAsync(request, ct);

            var id = Guid.NewGuid();
            intake.Orders[id] = new PendingOrder(request, DateTimeOffset.UtcNow);
            return new OrderSubmission(id, OrderDisplayState.Preparing, null, []);
        }

        public Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default) =>
            inner.CheckSourcesAsync(sources, ownerUsername, ct);

        public async Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
        {
            if (!intake.Orders.TryGetValue(orderId, out var pending))
                return await inner.GetAsync(orderId, requestingUsername, ct);
            if (!Visible(pending, requestingUsername))
                return null;

            await AdvanceAsync(pending, ct);
            if (pending.RealOrderId is not { } realId)
                return PendingView(orderId, pending);

            var view = await inner.GetAsync(realId, null, ct);
            return view is null ? null : view with { OrderId = orderId, Clarification = ClarificationOf(pending) };
        }

        public async Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
        {
            if (!intake.Orders.TryGetValue(orderId, out var pending))
                return await inner.GetResultAsync(orderId, requestingUsername, ct);
            if (!Visible(pending, requestingUsername) || pending.RealOrderId is not { } realId)
                return null;

            var result = await inner.GetResultAsync(realId, null, ct);
            return result is null ? null : result with { OrderId = orderId, Assumptions = intake.Clarification.Assumptions };
        }

        public Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default) =>
            inner.ListAsync(ownerUsername, limit, ct);

        public async Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default)
        {
            if (!intake.Orders.TryGetValue(orderId, out var pending))
                return await inner.WaitForChangeAsync(orderId, requestingUsername, knownState, maxWait, ct);

            var deadline = DateTimeOffset.UtcNow + (maxWait > TimeSpan.FromSeconds(50) ? TimeSpan.FromSeconds(50) : maxWait);
            while (true)
            {
                var view = await GetAsync(orderId, requestingUsername, ct);
                if (view is null || view.Display.IsFinished || view.Display.State != knownState || DateTimeOffset.UtcNow >= deadline)
                    return view;
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
        }

        public Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
            inner.CancelAsync(RealId(orderId), requestingUsername, ct);

        public Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
            inner.DeleteAsync(RealId(orderId), requestingUsername, ct);

        public Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default) =>
            StartAsync(request.OrderId, request.RequestingUsername, request.Answers, IntakeOutcome.Answered, ct);

        public Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
            StartAsync(orderId, requestingUsername, [], IntakeOutcome.Skipped, ct);

        public Task<OrderRevision> ReviseAsync(ReviseOrderRequest request, CancellationToken ct = default) =>
            inner.ReviseAsync(request, ct);

        private async Task<OrderSubmission> StartAsync(Guid orderId, string? requestingUsername, IReadOnlyList<OrderAnswer> answers, IntakeOutcome outcome, CancellationToken ct)
        {
            if (!intake.Orders.TryGetValue(orderId, out var pending) || !Visible(pending, requestingUsername))
                throw new OrderAnswerException("Order not found.");

            await AdvanceAsync(pending, ct);
            await pending.Lock.WaitAsync(ct);
            try
            {
                if (pending.RealOrderId is not null)
                    return new OrderSubmission(orderId, OrderDisplayState.Working, pending.RunId, [SimulatedIntake.AlreadyStartedNotice]);

                var known = intake.Clarification.Questions.Select(q => q.Id).Concat(intake.Clarification.Assumptions.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
                var wrong = answers.FirstOrDefault(a => !known.Contains(a.QuestionId));
                if (wrong is not null)
                    throw new OrderAnswerException($"There is no question {wrong.QuestionId} in this order.");

                var started = await StartRealAsync(pending, ct);
                pending.Answers = answers;
                pending.Outcome = outcome;
                return new OrderSubmission(orderId, started.State, started.RunId, []);
            }
            finally
            {
                pending.Lock.Release();
            }
        }

        private async Task AdvanceAsync(PendingOrder pending, CancellationToken ct)
        {
            if (pending.State != OrderDisplayState.Preparing || DateTimeOffset.UtcNow < pending.CreatedAt + intake.AnalysisTime)
                return;

            await pending.Lock.WaitAsync(ct);
            try
            {
                if (pending.State != OrderDisplayState.Preparing)
                    return;
                if (intake.Clarification.Questions.Count > 0)
                {
                    pending.State = OrderDisplayState.NeedsInput;
                    return;
                }

                await StartRealAsync(pending, ct);
                pending.Outcome = IntakeOutcome.Clarified;
            }
            finally
            {
                pending.Lock.Release();
            }
        }

        private async Task<OrderSubmission> StartRealAsync(PendingOrder pending, CancellationToken ct)
        {
            var started = await inner.SubmitAsync(pending.Request with { AllowQuestions = false }, ct);
            pending.RealOrderId = started.OrderId;
            pending.RunId = started.RunId;
            pending.State = started.State;
            return started;
        }

        private ClarificationView ClarificationOf(PendingOrder pending) =>
            intake.Clarification with { Outcome = pending.Outcome ?? IntakeOutcome.Asked, Answers = pending.Answers };

        private OrderView PendingView(Guid orderId, PendingOrder pending)
        {
            var now = DateTimeOffset.UtcNow;
            var needsInput = pending.State == OrderDisplayState.NeedsInput;
            return new OrderView(
                orderId, "Synthetic order", pending.Request.RequestText, pending.Request.Mode, pending.Request.Entry,
                needsInput ? OrderState.NeedsInput : OrderState.Analyzing,
                new OrderDisplay(pending.State, false, false),
                pending.CreatedAt, now, null, [], [],
                new OrderProgress([], needsInput ? "Your answers" : "Reading your request", now - pending.CreatedAt),
                [],
                needsInput ? ClarificationOf(pending) : null);
        }

        private Guid RealId(Guid orderId) =>
            intake.Orders.TryGetValue(orderId, out var pending) && pending.RealOrderId is { } realId ? realId : orderId;

        private static bool Visible(PendingOrder pending, string? requestingUsername) =>
            requestingUsername is null || requestingUsername == pending.Request.CreatedByUser;
    }
}
