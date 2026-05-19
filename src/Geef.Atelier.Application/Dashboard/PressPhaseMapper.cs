namespace Geef.Atelier.Application.Dashboard;

/// <summary>
/// Maps a sequence of Geef SDK event-type strings for an active run to a phase-rail index.
/// Phase rail: 0=Grounding, 1=Execution, 2=Evaluation, 3=Finalize.
/// </summary>
public static class PressPhaseMapper
{
    // Real Geef SDK event-type names (from PostgresEventSink observations)
    public const string PipelineStarted       = "PipelineStartedEvent";
    public const string ExecutionCompleted    = "ExecutionCompletedEvent";
    public const string EvaluationApproved   = "EvaluationApprovedEvent";
    public const string EvaluationRejected   = "EvaluationRejectedEvent";
    public const string PipelineCompleted     = "PipelineCompletedEvent";
    public const string PipelineFailed        = "PipelineFailedEvent";

    /// <summary>
    /// Derives the current phase (0–3) and iteration count from the run's event stream.
    /// </summary>
    /// <param name="eventTypes">Event-type strings in ascending timestamp order.</param>
    /// <returns>(phase 0–3, iteration number starting from 1)</returns>
    public static (int Phase, int Iteration) Map(IReadOnlyList<string> eventTypes)
    {
        var phase     = 0;
        var iteration = 1;

        foreach (var ev in eventTypes)
        {
            switch (ev)
            {
                case PipelineStarted:
                    phase = 1; // Execution phase begins
                    break;
                case ExecutionCompleted:
                    phase = 2; // Evaluation phase
                    break;
                case EvaluationApproved:
                    phase = 3; // Finalize phase
                    break;
                case EvaluationRejected:
                    phase = 1; // Back to Execution for next iteration
                    iteration++;
                    break;
            }
        }

        return (phase, iteration);
    }
}
