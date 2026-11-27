using System.Globalization;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>What the progress display needs to know about the intake of an order (stage 2).</summary>
public sealed record OrderIntakeProgress(
    OrderState State,
    IntakeOutcome? Outcome,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? QuestionsAskedAt,
    DateTimeOffset? AnsweredAt);

/// <summary>Maps the display state and the pipeline events of the current run to plain-English progress steps.</summary>
public static class OrderProgressMapper
{
    public const string UnderstandKey = "understand";
    public const string AnswersKey = "answers";
    public const string FilesKey = "files";
    public const string WriteKey = "write";
    public const string CheckKey = "check";
    public const string FixKey = "fix";
    public const string DoneKey = "done";

    public const string GroundingStarted = "GroundingStartedEvent";
    public const string GroundingCompleted = "GroundingCompletedEvent";
    public const string ExecutionStarted = "ExecutionStartedEvent";
    public const string ExecutionCompleted = "ExecutionCompletedEvent";
    public const string EvaluationApproved = "EvaluationApprovedEvent";
    public const string EvaluationRejected = "EvaluationRejectedEvent";
    public const string PipelineStarted = "PipelineStartedEvent";

    /// <summary>
    /// Builds the steps of the progress display: <c>files</c> (only with sources), <c>write</c>, <c>check</c>,
    /// <c>fix</c> (only when a second iteration began), <c>done</c>. Clarified orders (<paramref name="intake"/> given) start with <c>understand</c> and, when
    /// questions were asked, <c>answers</c>. The elapsed time counts from <paramref name="versionStartedAt"/> when it is given (a run that
    /// changes an earlier version counts from its own start, not from the order), otherwise from <paramref name="orderCreatedAt"/>.
    /// </summary>
    public static OrderProgress Map(OrderDisplay display, bool hasSources, IReadOnlyList<RunEventRow> events, DateTimeOffset orderCreatedAt, DateTimeOffset? finishedAt, DateTimeOffset now, OrderIntakeProgress? intake = null, DateTimeOffset? versionStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.At).ToList();
        var steps = DirectSteps(hasSources, ordered);

        steps.Add(new StepDraft(DoneKey, "Done"));
        Finish(steps, display, finishedAt);

        var elapsed = (finishedAt ?? now) - (versionStartedAt ?? orderCreatedAt);
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var result = IntakeSteps(intake, display, orderCreatedAt)
            .Concat(steps.Select(s => new OrderStep(s.Key, s.Label, s.Status, s.StartedAt, s.EndedAt)))
            .ToList();
        return new OrderProgress(result, CurrentLabel(result, display), elapsed);
    }

    /// <summary>The intake facts of <paramref name="order"/> for <see cref="Map"/>; null when the order was never clarified.</summary>
    public static OrderIntakeProgress? ToIntakeProgress(this OrderEntity order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.State is OrderState.Analyzing or OrderState.NeedsInput || order.IntakeOutcome is not null
            ? new OrderIntakeProgress(order.State, order.IntakeOutcome, order.IntakeStartedAt, order.IntakeFinishedAt, order.QuestionsAskedAt, order.AnsweredAt)
            : null;
    }

    private static IEnumerable<OrderStep> IntakeSteps(OrderIntakeProgress? intake, OrderDisplay display, DateTimeOffset orderCreatedAt)
    {
        if (intake is null)
            yield break;

        var stopped = display.State == OrderDisplayState.Stopped;
        var understandStatus = intake.State switch
        {
            OrderState.Analyzing => stopped ? OrderStepStatus.Skipped : OrderStepStatus.Active,
            _ when intake.Outcome is IntakeOutcome.Failed => OrderStepStatus.Skipped,
            _ when intake.Outcome is IntakeOutcome.Skipped && intake.QuestionsAskedAt is null => OrderStepStatus.Skipped,
            _ when stopped && intake.FinishedAt is null => OrderStepStatus.Skipped,
            _ => OrderStepStatus.Done,
        };
        var understandEnd = understandStatus == OrderStepStatus.Active ? null : intake.FinishedAt ?? intake.QuestionsAskedAt;
        yield return new OrderStep(UnderstandKey, "Reading your request", understandStatus, intake.StartedAt ?? orderCreatedAt, understandEnd);

        if (intake.QuestionsAskedAt is not { } asked)
            yield break;

        var answersStatus = intake.State switch
        {
            OrderState.NeedsInput => OrderStepStatus.Active,
            _ when intake.Outcome is IntakeOutcome.Answered => OrderStepStatus.Done,
            _ => OrderStepStatus.Skipped,
        };
        yield return new OrderStep(AnswersKey, "Your answers", answersStatus, asked, answersStatus == OrderStepStatus.Active ? null : intake.AnsweredAt);
    }

    private static List<StepDraft> DirectSteps(bool hasSources, List<RunEventRow> events)
    {
        var steps = new List<StepDraft>();
        if (hasSources)
            steps.Add(FilesStep(events));

        var write = new StepDraft(WriteKey, "Writing");
        var check = new StepDraft(CheckKey, "Checking");
        StepDraft? fix = null;

        foreach (var e in events)
        {
            var iteration = e.Iteration ?? 1;
            switch (e.EventType)
            {
                case ExecutionStarted when iteration <= 1:
                    write.Start(e.At);
                    break;
                case ExecutionCompleted when iteration <= 1:
                    write.End(e.At);
                    check.Start(e.At);
                    break;
                case EvaluationApproved or EvaluationRejected when iteration <= 1:
                    check.End(e.At);
                    break;
                case ExecutionStarted:
                    fix ??= new StepDraft(FixKey, "Fixing details");
                    fix.Start(e.At);
                    break;
                case EvaluationApproved or EvaluationRejected when fix is not null:
                    fix.End(e.At);
                    break;
            }
        }

        steps.Add(write);
        steps.Add(check);
        if (fix is not null)
            steps.Add(fix);
        return steps;
    }

    private static StepDraft FilesStep(List<RunEventRow> events)
    {
        var files = new StepDraft(FilesKey, "Reading your files");
        foreach (var e in events)
        {
            if (e.EventType is GroundingStarted or PipelineStarted)
                files.Start(e.At);
            else if (e.EventType is GroundingCompleted or ExecutionStarted)
                files.End(e.At);
        }

        return files;
    }

    private static void Finish(List<StepDraft> steps, OrderDisplay display, DateTimeOffset? finishedAt)
    {
        switch (display.State)
        {
            case OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints:
                foreach (var step in steps)
                {
                    if (step.Status == OrderStepStatus.Active)
                        step.End(finishedAt);
                    else if (step.Status == OrderStepStatus.Pending)
                        step.Status = step.Key == DoneKey ? OrderStepStatus.Done : OrderStepStatus.Skipped;
                }

                var done = steps[^1];
                done.StartedAt ??= finishedAt;
                done.EndedAt ??= finishedAt;
                break;

            case OrderDisplayState.Failed:
                var failed = steps.LastOrDefault(s => s.Status == OrderStepStatus.Active)
                             ?? steps.LastOrDefault(s => s.Status == OrderStepStatus.Done && s.Key != DoneKey);
                if (failed is not null)
                {
                    failed.Status = OrderStepStatus.Failed;
                    failed.EndedAt ??= finishedAt;
                }
                else
                {
                    steps[^1].Status = OrderStepStatus.Failed;
                }

                foreach (var step in steps.Where(s => s.Status == OrderStepStatus.Pending))
                    step.Status = OrderStepStatus.Skipped;
                break;

            case OrderDisplayState.Stopped:
                foreach (var step in steps.Where(s => s.Status is OrderStepStatus.Active or OrderStepStatus.Pending))
                {
                    step.Status = OrderStepStatus.Skipped;
                    if (step.StartedAt is not null)
                        step.EndedAt ??= finishedAt;
                }

                break;

            case OrderDisplayState.Queued or OrderDisplayState.Preparing or OrderDisplayState.NeedsInput:
                foreach (var step in steps)
                    step.Status = OrderStepStatus.Pending;
                break;
        }
    }

    private static string CurrentLabel(IReadOnlyList<OrderStep> steps, OrderDisplay display) => display.State switch
    {
        OrderDisplayState.Preparing => "Getting ready",
        OrderDisplayState.NeedsInput => "Needs your answers",
        OrderDisplayState.Queued => "Waiting to start",
        OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints => "Done",
        OrderDisplayState.Failed => "Something went wrong",
        OrderDisplayState.Stopped => "Stopped",
        _ => steps.LastOrDefault(s => s.Status == OrderStepStatus.Active)?.Label
             ?? steps.FirstOrDefault(s => s.Status == OrderStepStatus.Pending)?.Label
             ?? "Working",
    };

    private sealed class StepDraft(string key, string label)
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        public OrderStepStatus Status { get; set; } = OrderStepStatus.Pending;
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }

        public void Start(DateTimeOffset at)
        {
            StartedAt ??= at;
            EndedAt = null;
            Status = OrderStepStatus.Active;
        }

        public void End(DateTimeOffset? at)
        {
            StartedAt ??= at;
            EndedAt = at;
            Status = OrderStepStatus.Done;
        }
    }
}
