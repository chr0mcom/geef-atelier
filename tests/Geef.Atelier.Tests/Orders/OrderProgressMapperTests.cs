using Geef.Atelier.Application.Orders;
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

        var progress = OrderProgressMapper.Map(Working, OrderMode.Direct, hasSources: true, events, T0, null, T0.AddSeconds(30));

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

        var progress = OrderProgressMapper.Map(Working, OrderMode.Direct, hasSources: false, events, T0, null, T0.AddSeconds(40));

        Assert.Equal(["write", "check", "fix", "done"], progress.Steps.Select(s => s.Key));
        Assert.Equal("Fixing details", progress.CurrentLabel);
        Assert.Equal(OrderStepStatus.Done, progress.Steps[1].Status);

        var withoutFix = OrderProgressMapper.Map(Working, OrderMode.Direct, false, events.Take(3).ToList(), T0, null, T0.AddSeconds(40));
        Assert.DoesNotContain(withoutFix.Steps, s => s.Key == "fix");
    }

    [Fact]
    public void CarefulPathNamesTheReviewRound()
    {
        var events = new List<RunEventRow>
        {
            new("ExecutionStartedEvent", 1, T0.AddSeconds(1)),
            new("ExecutionCompletedEvent", 1, T0.AddSeconds(10)),
            new("EvaluationRejectedEvent", 1, T0.AddSeconds(20)),
            new("ExecutionStartedEvent", 2, T0.AddSeconds(21)),
        };

        var progress = OrderProgressMapper.Map(Working, OrderMode.Careful, hasSources: false, events, T0, null, T0.AddSeconds(40));

        Assert.Equal(["write", "review", "done"], progress.Steps.Select(s => s.Key));
        Assert.Equal("Reviewing (round 2)", progress.CurrentLabel);
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

        var progress = OrderProgressMapper.Map(display, OrderMode.Direct, hasSources: true, events, T0, T0.AddSeconds(12), T0.AddMinutes(9));

        Assert.All(progress.Steps.Where(s => s.Key != "files"), s => Assert.Equal(OrderStepStatus.Done, s.Status));
        Assert.Equal("Done", progress.CurrentLabel);
        Assert.Equal(TimeSpan.FromSeconds(12), progress.Elapsed);
    }

    [Fact]
    public void FailedMarksTheActiveStepAsFailedAndQueuedKeepsAllPending()
    {
        var events = new List<RunEventRow> { new("ExecutionStartedEvent", 1, T0.AddSeconds(1)) };
        var failed = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Failed, false, true), OrderMode.Direct, false, events, T0, T0.AddSeconds(5), T0.AddSeconds(5));
        Assert.Equal(OrderStepStatus.Failed, failed.Steps.Single(s => s.Key == "write").Status);
        Assert.Equal("Something went wrong", failed.CurrentLabel);

        var queued = OrderProgressMapper.Map(new OrderDisplay(OrderDisplayState.Queued, false, false), OrderMode.Direct, true, [], T0, null, T0.AddSeconds(2));
        Assert.All(queued.Steps, s => Assert.Equal(OrderStepStatus.Pending, s.Status));
        Assert.Equal("Waiting to start", queued.CurrentLabel);
    }

    private static OrderDisplay Working => new(OrderDisplayState.Working, false, false);
}
