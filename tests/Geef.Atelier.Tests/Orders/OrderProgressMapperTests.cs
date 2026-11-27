using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Tests.Orders;

public sealed class OrderProgressMapperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DirectPathWithFilesShowsReadingThenWritingWhileWorking()
    {
        var events = new List<RunEventRow>
        {
            new("PipelineStartedEvent", null, T0.AddSeconds(1)),
            new("GroundingStartedEvent", null, T0.AddSeconds(1)),
            new("GroundingCompletedEvent", null, T0.AddSeconds(3)),
            new("ExecutionStartedEvent", 1, T0.AddSeconds(4)),
        };

        var progress = OrderProgressMapper.Map(Working, hasSources: true, events, T0, null, T0.AddSeconds(30));

        Assert.Equal(["files", "write", "check", "done"], progress.Steps.Select(s => s.Key));
        Assert.Equal(OrderStepStatus.Done, progress.Steps[0].Status);
        Assert.Equal(OrderStepStatus.Active, progress.Steps[1].Status);
        Assert.Equal("Writing", progress.CurrentLabel);
        Assert.Equal(TimeSpan.FromSeconds(30), progress.Elapsed);
    }

    [Fact]
    public void DirectPathAddsTheFixStepOnlyWhenASecondIterationBegan()
    {
        var events = new List<RunEventRow>
        {
            new("ExecutionStartedEvent", 1, T0.AddSeconds(1)),
            new("ExecutionCompletedEvent", 1, T0.AddSeconds(20)),
            new("EvaluationRejectedEvent", 1, T0.AddSeconds(21)),
            new("ExecutionStartedEvent", 2, T0.AddSeconds(22)),
        };

        var progress = OrderProgressMapper.Map(Working, hasSources: false, events, T0, null, T0.AddSeconds(40));

        Assert.Equal(["write", "check", "fix", "done"], progress.Steps.Select(s => s.Key));
        Assert.Equal("Fixing details", progress.CurrentLabel);
        Assert.Equal(OrderStepStatus.Done, progress.Steps[1].Status);

        var withoutFix = OrderProgressMapper.Map(Working, false, events.Take(3).ToList(), T0, null, T0.AddSeconds(40));
        Assert.DoesNotContain(withoutFix.Steps, s => s.Key == "fix");
    }

    [Fact]
    public void DeliveredMarksEveryStepDoneAndUsesTheFinishTime()
    {
        var events = new List<RunEventRow>
        {
            new("ExecutionStartedEvent", 1, T0.AddSeconds(1)),
            new("ExecutionCompletedEvent", 1, T0.AddSeconds(10)),
            new("EvaluationApprovedEvent", 1, T0.AddSeconds(11)),
        };
        var display = new OrderDisplay(OrderDisplayState.Delivered, true, true);

        var progress = OrderProgressMapper.Map(display, hasSources: true, events, T0, T0.AddSeconds(12), T0.AddMinutes(9));

        Assert.All(progress.Steps.Where(s => s.Key != "files"), s => Assert.Equal(OrderStepStatus.Done, s.Status));
        Assert.Equal("Done", progress.CurrentLabel);
        Assert.Equal(TimeSpan.FromSeconds(12), progress.Elapsed);
    }

    [Fact]
    public void FailedMarksTheActiveStepAsFailedAndQueuedKeepsAllPending()
    {
        var events = new List<RunEventRow> { new("ExecutionStartedEvent", 1, T0.AddSeconds(1)) };
        var failed = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Failed, false, true), false, events, T0, T0.AddSeconds(5), T0.AddSeconds(5));
        Assert.Equal(OrderStepStatus.Failed, failed.Steps.Single(s => s.Key == "write").Status);
        Assert.Equal("Something went wrong", failed.CurrentLabel);

        var queued = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Queued, false, false), true, [], T0, null, T0.AddSeconds(2));
        Assert.All(queued.Steps, s => Assert.Equal(OrderStepStatus.Pending, s.Status));
        Assert.Equal("Waiting to start", queued.CurrentLabel);
    }

    [Fact]
    public void OrdersWithoutIntakeKeepTheirSteps()
    {
        var order = OrderTestData.Order("user-a", at: T0);

        Assert.Null(order.ToIntakeProgress());
        Assert.Equal(["write", "check", "done"], OrderProgressMapper.Map(Working, false, [], T0, null, T0, order.ToIntakeProgress()).Steps.Select(s => s.Key));
    }

    [Fact]
    public void ReadingTheRequestIsActiveWhileAnalysing()
    {
        var order = OrderTestData.Order("user-a", state: OrderState.Analyzing, at: T0) with { IntakeStartedAt = T0.AddSeconds(2) };
        var preparing = new OrderDisplay(OrderDisplayState.Preparing, false, false);

        var progress = OrderProgressMapper.Map(preparing, false, [], T0, null, T0.AddSeconds(5), order.ToIntakeProgress());

        Assert.Equal(["understand", "write", "check", "done"], progress.Steps.Select(s => s.Key));
        Assert.Equal(("Reading your request", OrderStepStatus.Active, T0.AddSeconds(2), (DateTimeOffset?)null),
            (progress.Steps[0].Label, progress.Steps[0].Status, progress.Steps[0].StartedAt!.Value, progress.Steps[0].EndedAt));
        Assert.Equal("Getting ready", progress.CurrentLabel);
        Assert.All(progress.Steps.Skip(1), s => Assert.Equal(OrderStepStatus.Pending, s.Status));
    }

    [Fact]
    public void QuestionsAddTheAnswersStepUntilTheyAreAnswered()
    {
        var waiting = OrderTestData.Order("user-a", state: OrderState.NeedsInput, at: T0) with
        {
            IntakeOutcome = IntakeOutcome.Asked, IntakeStartedAt = T0, IntakeFinishedAt = T0.AddSeconds(9), QuestionsAskedAt = T0.AddSeconds(9),
        };
        var needsInput = new OrderDisplay(OrderDisplayState.NeedsInput, false, false);

        var asked = OrderProgressMapper.Map(needsInput, false, [], T0, null, T0.AddMinutes(1), waiting.ToIntakeProgress());
        Assert.Equal(["understand", "answers", "write", "check", "done"], asked.Steps.Select(s => s.Key));
        Assert.Equal((OrderStepStatus.Done, OrderStepStatus.Active), (asked.Steps[0].Status, asked.Steps[1].Status));
        Assert.Equal("Your answers", asked.Steps[1].Label);
        Assert.Equal("Needs your answers", asked.CurrentLabel);

        var answered = waiting with { State = OrderState.Ready, IntakeOutcome = IntakeOutcome.Answered, AnsweredAt = T0.AddMinutes(2) };
        var steps = OrderProgressMapper.Map(Working, false, [new("ExecutionStartedEvent", 1, T0.AddMinutes(3))], T0, null, T0.AddMinutes(4), answered.ToIntakeProgress()).Steps;
        Assert.Equal((OrderStepStatus.Done, T0.AddMinutes(2)), (steps[1].Status, steps[1].EndedAt!.Value));
        Assert.Equal(OrderStepStatus.Active, steps[2].Status);

        var auto = waiting with { State = OrderState.Ready, IntakeOutcome = IntakeOutcome.AutoStarted, AnsweredAt = T0.AddDays(1) };
        Assert.Equal(OrderStepStatus.Skipped, OrderProgressMapper.Map(Working, false, [], T0, null, T0.AddDays(1), auto.ToIntakeProgress()).Steps[1].Status);
        var stopped = waiting with { State = OrderState.Cancelled };
        var stoppedSteps = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Stopped, false, true), false, [], T0, T0.AddMinutes(1), T0.AddMinutes(1), stopped.ToIntakeProgress()).Steps;
        Assert.Equal((OrderStepStatus.Done, OrderStepStatus.Skipped), (stoppedSteps[0].Status, stoppedSteps[1].Status));
    }

    [Fact]
    public void SkippingOrAFailedIntakeShowsReadingAsSkipped()
    {
        var skipped = OrderTestData.Order("user-a", at: T0) with { IntakeOutcome = IntakeOutcome.Skipped, IntakeFinishedAt = T0.AddSeconds(3) };
        var failed = skipped with { IntakeOutcome = IntakeOutcome.Failed };
        var clarified = skipped with { IntakeOutcome = IntakeOutcome.Clarified };
        var stoppedWhileReading = OrderTestData.Order("user-a", state: OrderState.Analyzing, at: T0);
        var stoppedDisplay = new OrderDisplay(OrderDisplayState.Stopped, false, true);

        Assert.Equal(OrderStepStatus.Skipped, OrderProgressMapper.Map(Working, false, [], T0, null, T0, skipped.ToIntakeProgress()).Steps[0].Status);
        Assert.Equal(OrderStepStatus.Skipped, OrderProgressMapper.Map(Working, false, [], T0, null, T0, failed.ToIntakeProgress()).Steps[0].Status);
        var done = OrderProgressMapper.Map(Working, false, [], T0, null, T0, clarified.ToIntakeProgress()).Steps;
        Assert.Equal((OrderStepStatus.Done, T0, T0.AddSeconds(3)), (done[0].Status, done[0].StartedAt!.Value, done[0].EndedAt!.Value));
        Assert.DoesNotContain(done, s => s.Key == OrderProgressMapper.AnswersKey);
        Assert.Equal(OrderStepStatus.Skipped, OrderProgressMapper.Map(stoppedDisplay, false, [], T0, T0, T0, stoppedWhileReading.ToIntakeProgress()).Steps[0].Status);
    }

    [Fact]
    public void A_change_counts_the_time_from_the_start_of_its_version_not_from_the_order()
    {
        var versionStart = T0.AddDays(1);
        var events = new List<RunEventRow> { new("ExecutionStartedEvent", 1, versionStart.AddSeconds(2)) };

        var working = OrderProgressMapper.Map(Working, false, events, T0, null, versionStart.AddSeconds(8), versionStartedAt: versionStart);
        var delivered = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Delivered, true, true), false, events, T0,
            versionStart.AddSeconds(95), versionStart.AddHours(3), versionStartedAt: versionStart);
        var first = OrderProgressMapper.Map(Working, false, events, T0, null, versionStart.AddSeconds(8));

        Assert.Equal(TimeSpan.FromSeconds(8), working.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(95), delivered.Elapsed);
        Assert.Equal(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(8), first.Elapsed);
    }

    private static OrderDisplay Working => new(OrderDisplayState.Working, false, false);
}
