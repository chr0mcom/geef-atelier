using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The intake side of <see cref="IOrderService"/> over the real wiring (orchestrator held, intake worker removed so the
/// tests play its part through the repository): the switch per mode, answers, skipping, stopping and the run starter.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderServiceIntakeTests(PostgresFixture postgres)
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.";

    [Fact]
    public async Task On_creates_an_analyzing_order_without_a_run_and_wakes_the_worker()
    {
        var signal = new RecordingSignal();
        await using var host = await StartAsync(IntakeMode.On, signal);
        var user = NewUser();

        var submission = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));

        Assert.Equal(OrderDisplayState.Preparing, submission.State);
        Assert.Null(submission.RunId);
        Assert.Equal(1, signal.Wakes);
        var order = await LoadAsync(host, submission.OrderId);
        Assert.Equal(OrderState.Analyzing, order.State);
        Assert.Null(order.IntakeOutcome);
        Assert.Equal(0, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == submission.OrderId)));
        Assert.False(await host.QueryAsync(db => db.OrderIntakeShadows.AnyAsync(s => s.OrderId == submission.OrderId)));

        var view = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user));
        Assert.Equal(OrderDisplayState.Preparing, view!.Display.State);
        Assert.Null(view.Clarification);
        var understand = view.Progress.Steps[0];
        Assert.Equal((OrderProgressMapper.UnderstandKey, OrderStepStatus.Active), (understand.Key, understand.Status));

        var bypassed = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Mcp, AllowQuestions: false));
        Assert.Equal(OrderDisplayState.Queued, bypassed.State);
        Assert.NotNull(bypassed.RunId);
        var tooLong = await SubmitAsync(host, new SubmitOrderRequest(new string('x', 8_001), user, OrderEntry.Web));
        Assert.NotNull(tooLong.RunId);
        Assert.Equal(1, signal.Wakes);
        Assert.Null((await LoadAsync(host, bypassed.OrderId)).IntakeOutcome);
    }

    [Fact]
    public async Task Shadow_starts_the_run_and_queues_a_shadow_row_while_off_stays_stage_one()
    {
        var signal = new RecordingSignal();
        string shadowSnapshot;
        await using (var host = await StartAsync(IntakeMode.Shadow, signal))
        {
            var user = NewUser();
            var submission = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));

            Assert.Equal(OrderDisplayState.Queued, submission.State);
            var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
            Assert.Equal(Request, run.BriefingText);
            shadowSnapshot = run.CrewSnapshot!;
            var shadow = await host.QueryAsync(db => db.OrderIntakeShadows.AsNoTracking().SingleAsync(s => s.OrderId == submission.OrderId));
            Assert.Equal(IntakeShadowStatuses.Pending, shadow.Status);
            Assert.Equal(1, signal.Wakes);
            Assert.Equal(OrderState.Ready, (await LoadAsync(host, submission.OrderId)).State);

            var bypassed = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Mcp, AllowQuestions: false));
            Assert.False(await host.QueryAsync(db => db.OrderIntakeShadows.AnyAsync(s => s.OrderId == bypassed.OrderId)));
        }

        var offSignal = new RecordingSignal();
        await using (var host = await StartAsync(IntakeMode.Off, offSignal))
        {
            var user = NewUser();
            var submission = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));

            var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
            Assert.Equal(Request, run.BriefingText);
            Assert.Equal(shadowSnapshot, run.CrewSnapshot);
            Assert.Equal(SystemCrew.DirectTemplateName, run.CrewTemplateName);
            Assert.False(await host.QueryAsync(db => db.OrderIntakeShadows.AnyAsync(s => s.OrderId == submission.OrderId)));
            Assert.Equal(0, offSignal.Wakes);
            var view = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user));
            Assert.Null(view!.Clarification);
            Assert.DoesNotContain(view.Progress.Steps, s => s.Key == OrderProgressMapper.UnderstandKey);

            await host.WithScopeAsync(sp => sp.GetRequiredService<IProfileService>()
                .SaveAsync(user, new UserProfileInput("Anna Beispiel", null, null, Formality.Informal, null, null, null, null)));
            var withProfile = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));
            var profiled = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == withProfile.RunId));
            Assert.Equal(
                Request + "\n\n---\n" + IntakeBriefComposer.ProfileHeading + "\nName: Anna Beispiel\nAddress the readers: informally",
                profiled.BriefingText);
        }
    }

    [Fact]
    public async Task Answers_start_the_run_with_the_answer_block_and_a_repeat_changes_nothing()
    {
        await using var host = await StartAsync(IntakeMode.On);
        var user = NewUser();
        var orderId = await WaitingForAnswersAsync(host, user, IntakeBriefComposerTests.Record());

        var view = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(orderId, user));
        Assert.Equal(OrderDisplayState.NeedsInput, view!.Display.State);
        var clarification = view.Clarification!;
        Assert.Equal((IntakeOutcome.Asked, "Einladung", "de"), (clarification.Outcome!.Value, clarification.TextType, clarification.Language));
        Assert.Equal(["length", "q1", "q2"], clarification.Questions.Select(q => q.Id));
        Assert.Equal(["a1", "a2"], clarification.Assumptions.Select(a => a.Id));
        Assert.Equal(clarification.QuestionsAskedAt!.Value + TimeSpan.FromHours(24), clarification.AutoStartAt);
        Assert.Equal(["understand", "answers"], view.Progress.Steps.Take(2).Select(s => s.Key));
        Assert.Equal([OrderStepStatus.Done, OrderStepStatus.Active], view.Progress.Steps.Take(2).Select(s => s.Status));
        var waiting = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, user));
        Assert.Equal(OrderService.NeedsAnswersNote, waiting!.Note);
        Assert.Equal(["a1", "a2"], waiting.Assumptions!.Select(a => a.Id));

        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(orderId, user,
        [
            new OrderAnswer("length", "short", null, false, AnswerOrigin.User),
            new OrderAnswer("a1", null, "Eher förmlich", false, AnswerOrigin.User),
        ])));

        Assert.Equal(OrderDisplayState.Queued, submission.State);
        Assert.NotNull(submission.RunId);
        Assert.Empty(submission.Notices);
        Assert.Equal(IntakeOutcome.Answered, submission.Clarification!.Outcome);
        Assert.Equal(["length", "q1", "q2", "a1"], submission.Clarification.Answers.Select(a => a.QuestionId));
        Assert.True(submission.Clarification.Answers[1].DecideForMe);

        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.Equal(submission.RunId, run.Id);
        Assert.Equal(1, run.OrderVersion);
        Assert.Equal(
            Request + "\n\n---\n" +
            "Answers the person gave before writing:\n" +
            "- Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)\n" +
            "- Wer kommt? → left to you\n" +
            "- Gibt es ein Motto? → left to you\n" +
            "Corrections to assumptions:\n" +
            "- Lockerer, herzlicher Ton → Eher förmlich",
            run.BriefingText);
        var order = await LoadAsync(host, orderId);
        Assert.Equal((OrderState.Ready, IntakeOutcome.Answered), (order.State, order.IntakeOutcome!.Value));
        Assert.NotNull(order.AnsweredAt);

        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, user));
        Assert.Equal(["a2"], result!.Assumptions!.Select(a => a.Id));

        var repeat = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(orderId, user,
            [new OrderAnswer("length", "long", null, false, AnswerOrigin.User)])));
        Assert.Equal([OrderService.AlreadyStartedNotice], repeat.Notices);
        Assert.Equal(run.Id, repeat.RunId);
        var skipAfter = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(orderId, user));
        Assert.Equal([OrderService.AlreadyStartedNotice], skipAfter.Notices);
        Assert.Equal(1, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == orderId)));
    }

    [Fact]
    public async Task Invalid_answers_change_nothing_and_foreign_or_missing_orders_are_not_found()
    {
        await using var host = await StartAsync(IntakeMode.On);
        var user = NewUser();
        var orderId = await WaitingForAnswersAsync(host, user, IntakeBriefComposerTests.Record());

        foreach (var (answers, message) in new (OrderAnswer[], string)[]
                 {
                     ([new OrderAnswer("q7", null, null, true, AnswerOrigin.User)], "There is no question \"q7\" for this order."),
                     ([new OrderAnswer("q1", "o9", null, false, AnswerOrigin.User)], "\"o9\" is not an answer option of question \"q1\"."),
                     ([new OrderAnswer("q1", null, new string('x', 501), false, AnswerOrigin.Client)], "The answer to \"q1\" is too long (501 characters, at most 500)."),
                     ([new OrderAnswer("q1", "o1", null, false, AnswerOrigin.Memory)], "The answer to \"q1\" needs the origin \"user\" or \"client\"."),
                     ([new OrderAnswer("q1", "o1", null, false, AnswerOrigin.User), new OrderAnswer("q1", "o2", null, false, AnswerOrigin.User)], "The question \"q1\" was answered more than once."),
                     ([new OrderAnswer("revision", "no", null, false, AnswerOrigin.User)], "There is no question \"revision\" for this order."),
                 })
        {
            var ex = await Assert.ThrowsAsync<OrderAnswerException>(() =>
                host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(orderId, user, answers))));
            Assert.Equal(message, ex.Message);
        }

        Assert.Equal(OrderState.NeedsInput, (await LoadAsync(host, orderId)).State);
        Assert.Equal(0, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == orderId)));

        var foreign = await Assert.ThrowsAsync<OrderAnswerException>(() =>
            host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(orderId, NewUser(), []))));
        Assert.Equal(OrderService.OrderNotFoundMessage, foreign.Message);
        var missing = await Assert.ThrowsAsync<OrderAnswerException>(() =>
            host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(Guid.NewGuid(), user)));
        Assert.Equal(OrderService.OrderNotFoundMessage, missing.Message);
    }

    [Fact]
    public async Task Skipping_works_while_reading_and_while_waiting_and_stopping_ends_the_intake()
    {
        await using var host = await StartAsync(IntakeMode.On);
        var user = NewUser();

        var reading = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));
        var tooEarly = await Assert.ThrowsAsync<OrderAnswerException>(() =>
            host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(reading.OrderId, user, []))));
        Assert.Equal(OrderService.StillReadingMessage, tooEarly.Message);
        var skipped = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(reading.OrderId, user));
        Assert.Equal(OrderDisplayState.Queued, skipped.State);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == reading.OrderId));
        Assert.Equal(Request, run.BriefingText);
        var order = await LoadAsync(host, reading.OrderId);
        Assert.Equal((OrderState.Ready, IntakeOutcome.Skipped), (order.State, order.IntakeOutcome!.Value));
        Assert.NotNull(order.IntakeFinishedAt);
        Assert.Null(order.ClarifyJson);
        Assert.Equal(IntakeOutcome.Skipped, skipped.Clarification!.Outcome);
        var skippedView = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(reading.OrderId, user));
        Assert.Equal(OrderStepStatus.Skipped, skippedView!.Progress.Steps.Single(s => s.Key == OrderProgressMapper.UnderstandKey).Status);

        var waitingId = await WaitingForAnswersAsync(host, user, IntakeBriefComposerTests.Record());
        var skippedWaiting = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(waitingId, user));
        Assert.NotNull(skippedWaiting.RunId);
        var waitingOrder = await LoadAsync(host, waitingId);
        Assert.Equal((IntakeOutcome.Skipped, (string?)null), (waitingOrder.IntakeOutcome!.Value, waitingOrder.AnswersJson));
        Assert.NotNull(waitingOrder.AnsweredAt);
        Assert.NotNull(waitingOrder.ClarifyJson);
        Assert.Equal(Request, (await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == waitingId))).BriefingText);

        var stopped = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));
        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().CancelAsync(stopped.OrderId, user)));
        var afterStop = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(stopped.OrderId, user));
        Assert.Equal([OrderService.StoppedNotice], afterStop.Notices);
        Assert.Equal((OrderDisplayState.Stopped, (Guid?)null), (afterStop.State, afterStop.RunId));
        var answerAfterStop = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(stopped.OrderId, user, [])));
        Assert.Equal([OrderService.StoppedNotice], answerAfterStop.Notices);
        Assert.Equal(0, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == stopped.OrderId)));
        await using var scope = host.Services.CreateAsyncScope();
        var lateStart = await scope.ServiceProvider.GetRequiredService<IOrderRunStarter>().TryStartAsync(
            new RunStartRequest(await LoadAsync(host, stopped.OrderId) with { State = OrderState.Analyzing }, OrderState.Analyzing, null, [], IntakeOutcome.Clarified, true), default);
        Assert.Null(lateStart);
    }

    [Fact]
    public async Task The_run_starter_reports_intake_warnings_stores_a_new_record_and_sets_the_revision_role_only_in_the_direct_path()
    {
        await using var host = await StartAsync(IntakeMode.On);
        var user = NewUser();

        var failedOrder = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));
        var fallback = IntakeRecord.Fallback(IntakeRecordStatus.TimedOut, "timeout", 40_000, null, BriefMode.AppendAnswers);
        var failedRun = await StartAsync(host, failedOrder.OrderId, OrderState.Analyzing, fallback, IntakeOutcome.Failed, recordIsNew: true);
        Assert.NotNull(failedRun);
        var failed = await LoadAsync(host, failedOrder.OrderId);
        Assert.Equal(IntakeRecordStatus.TimedOut, IntakeRecordJson.TryParse(failed.ClarifyJson)!.Status);
        var failedWarning = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().SingleAsync(w => w.RunId == failedRun));
        Assert.Equal((RunWarningSources.Intake, RunWarningCodes.IntakeFailed, IntakeWarningMessages.Failed), (failedWarning.Source, failedWarning.Code, failedWarning.Message));
        var failedView = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(failedOrder.OrderId, user));
        Assert.Equal(OrderStepStatus.Skipped, failedView!.Progress.Steps[0].Status);

        var lateId = await WaitingForAnswersAsync(host, user, IntakeBriefComposerTests.Record());
        var lateRun = await StartAsync(host, lateId, OrderState.NeedsInput, IntakeRecordJson.TryParse((await LoadAsync(host, lateId)).ClarifyJson), IntakeOutcome.AutoStarted, recordIsNew: false);
        var lateWarning = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().SingleAsync(w => w.RunId == lateRun));
        Assert.Equal((RunWarningCodes.IntakeUnanswered, IntakeWarningMessages.Unanswered), (lateWarning.Code, lateWarning.Message));
        var lateView = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(lateId, user));
        Assert.Equal([OrderStepStatus.Done, OrderStepStatus.Skipped], lateView!.Progress.Steps.Take(2).Select(s => s.Status));

        var direct = await RevisionCaseAsync(host, user, OrderMode.Direct, answers: []);
        Assert.Equal(SourceRole.TemplateToRevise, direct.Role);
        Assert.Equal("letter.md", direct.FileName);
        var declined = await RevisionCaseAsync(host, user, OrderMode.Direct, answers: [new OrderAnswer("revision", "no", null, false, AnswerOrigin.User)]);
        Assert.Equal(SourceRole.Facts, declined.Role);
        var careful = await RevisionCaseAsync(host, user, OrderMode.Careful, answers: []);
        Assert.Equal(SourceRole.Facts, careful.Role);
    }

    private static async Task<(SourceRole Role, string? FileName)> RevisionCaseAsync(AtelierEndToEndHost host, string user, OrderMode mode, OrderAnswer[] answers)
    {
        var receipt = await host.WithScopeAsync(sp => sp.GetRequiredService<ISourceIntakeService>()
            .IntakeTextAsync("letter.md", "Sehr geehrte Frau Beispiel, " + Guid.NewGuid(), user, SourceOrigin.WebUpload));
        var sourceId = receipt.SourceId!.Value;
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Überarbeite den Brief.", user, OrderEntry.Web,
            [new OrderSourceSelection(sourceId)], mode, mode == OrderMode.Careful ? SystemCrew.KlassikTemplateName : null));
        var record = IntakeBriefComposerTests.Record() with { RevisionSourceId = sourceId };
        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderRepository>()
            .TrySaveQuestionsAsync(submission.OrderId, IntakeRecordJson.Serialize(record), DateTimeOffset.UtcNow)));

        var view = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user));
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(submission.OrderId, user, answers)));

        var role = await host.QueryAsync(db => db.OrderSources.AsNoTracking().Where(l => l.OrderId == submission.OrderId).Select(l => l.Role).SingleAsync());
        return (role, view!.Clarification!.RevisionFileName);
    }

    private static Task<Guid?> StartAsync(AtelierEndToEndHost host, Guid orderId, OrderState expected, IntakeRecord? record, IntakeOutcome outcome, bool recordIsNew) =>
        host.WithScopeAsync(async sp =>
        {
            var order = await sp.GetRequiredService<IOrderRepository>().GetAsync(orderId);
            return await sp.GetRequiredService<IOrderRunStarter>().TryStartAsync(new RunStartRequest(order!, expected, record, [], outcome, recordIsNew), default);
        });

    private static async Task<Guid> WaitingForAnswersAsync(AtelierEndToEndHost host, string user, IntakeRecord record)
    {
        var submission = await SubmitAsync(host, new SubmitOrderRequest(Request, user, OrderEntry.Web));
        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderRepository>()
            .TrySaveQuestionsAsync(submission.OrderId, IntakeRecordJson.Serialize(record), DateTimeOffset.UtcNow)));
        return submission.OrderId;
    }

    private Task<AtelierEndToEndHost> StartAsync(IntakeMode mode, RecordingSignal? signal = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<OrderOptions>(o => o.IntakeMode = mode);
            foreach (var worker in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "OrderIntakeWorker").ToList())
                services.Remove(worker);
            services.AddSingleton<IOrderIntakeSignal>(signal ?? new RecordingSignal());
        });

    private static Task<OrderSubmission> SubmitAsync(AtelierEndToEndHost host, SubmitOrderRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(request));

    private static Task<OrderEntity> LoadAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));

    private sealed class RecordingSignal : IOrderIntakeSignal
    {
        private int _wakes;

        public int Wakes => Volatile.Read(ref _wakes);

        public void Wake() => Interlocked.Increment(ref _wakes);

        public ValueTask WaitAsync(TimeSpan max, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
