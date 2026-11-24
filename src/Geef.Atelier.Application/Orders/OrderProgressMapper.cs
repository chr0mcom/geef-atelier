using System.Globalization;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>Maps the display state and the pipeline events of the current run to plain-English progress steps.</summary>
public static class OrderProgressMapper
{
    public const string FilesKey = "files";
    public const string WriteKey = "write";
    public const string CheckKey = "check";
    public const string FixKey = "fix";
    public const string ReviewKey = "review";
    public const string DoneKey = "done";

    public const string GroundingStarted = "GroundingStartedEvent";
    public const string GroundingCompleted = "GroundingCompletedEvent";
    public const string ExecutionStarted = "ExecutionStartedEvent";
    public const string ExecutionCompleted = "ExecutionCompletedEvent";
    public const string EvaluationApproved = "EvaluationApprovedEvent";
    public const string EvaluationRejected = "EvaluationRejectedEvent";
    public const string PipelineStarted = "PipelineStartedEvent";
    public const string PipelineCompleted = "PipelineCompletedEvent";
    public const string PipelineFailed = "PipelineFailedEvent";

    /// <summary>
    /// Builds the steps of the progress display: direct path <c>files</c> (only with sources), <c>write</c>, <c>check</c>,
    /// <c>fix</c> (only when a second iteration began), <c>done</c>; careful path <c>files</c> (only with sources), <c>write</c>,
    /// <c>review</c>, <c>done</c>.
    /// </summary>
    public static OrderProgress Map(OrderDisplay display, OrderMode mode, bool hasSources, IReadOnlyList<RunEventRow> events, DateTimeOffset orderCreatedAt, DateTimeOffset? finishedAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(events);

        var ordered = events.OrderBy(e => e.At).ToList();
        var steps = mode == OrderMode.Direct
            ? DirectSteps(hasSources, ordered)
            : CarefulSteps(hasSources, ordered);

        steps.Add(new StepDraft(DoneKey, "Done"));
        Finish(steps, display, finishedAt);

        var elapsed = (finishedAt ?? now) - orderCreatedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var result = steps.Select(s => new OrderStep(s.Key, s.Label, s.Status, s.StartedAt, s.EndedAt)).ToList();
        return new OrderProgress(result, CurrentLabel(result, display), elapsed);
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

    private static List<StepDraft> CarefulSteps(bool hasSources, List<RunEventRow> events)
    {
        var steps = new List<StepDraft>();
        if (hasSources)
            steps.Add(FilesStep(events));

        var write = new StepDraft(WriteKey, "Writing");
        var review = new StepDraft(ReviewKey, "Reviewing");
        var round = 0;

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
                    review.Start(e.At);
                    round = Math.Max(round, 1);
                    break;
                case ExecutionStarted or ExecutionCompleted or EvaluationApproved or EvaluationRejected:
                    round = Math.Max(round, iteration);
                    break;
                case PipelineCompleted or PipelineFailed:
                    review.End(e.At);
                    break;
            }
        }

        if (round > 0)
            review.Label = string.Create(CultureInfo.InvariantCulture, $"Reviewing (round {round})");

        steps.Add(write);
        steps.Add(review);
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
        public string Label { get; set; } = label;
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
