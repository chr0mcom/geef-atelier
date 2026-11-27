namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>Stable machine-readable codes of <see cref="RunWarningEntity"/> rows, exposed to MCP clients.</summary>
public static class RunWarningCodes
{
    /// <summary>A reviewer could not complete its check in a round and was left out of the verdict.</summary>
    public const string ReviewerFailed = "review.reviewer_failed";

    /// <summary>The model matrix could not assign the planned model to an actor; another one was used.</summary>
    public const string ModelAssignment = "model.assignment";

    /// <summary>The AI gateway answered from another service than requested (failover); model plurality may not hold.</summary>
    public const string LlmFailover = "llm.failover";



    /// <summary>The run finished, but its status or final text could not be stored; it was recorded as failed.</summary>
    public const string ResultNotSaved = "run.result_not_saved";

    /// <summary>
    /// The service was restarted while the finishing steps ran; the outcome was recorded on the next start and the
    /// result may be incomplete.
    /// </summary>
    public const string RestartedWhileFinishing = "run.restarted_while_finishing";

    /// <summary>The intake could not prepare questions; writing started with the request as written (<see cref="IntakeWarningMessages.Failed"/>).</summary>
    public const string IntakeFailed = "intake.failed";

    /// <summary>Nobody answered the questions in time; writing started with the assumptions (<see cref="IntakeWarningMessages.Unanswered"/>).</summary>
    public const string IntakeUnanswered = "intake.unanswered";

    /// <summary>The block with the writer's assumptions could not be read (<see cref="WriterNotesWarningMessages.Unreadable"/>).</summary>
    public const string WriterNotesUnreadable = "writer.notes_unreadable";

    /// <summary>Looking things up on the web failed; the text was written without it (message <c>OrderResearchLines.Failed</c>).</summary>
    public const string ResearchFailed = "grounding.research_failed";
}

/// <summary>Plain English messages of the warnings about the writer's notes.</summary>
public static class WriterNotesWarningMessages
{
    /// <summary>Message of <see cref="RunWarningCodes.WriterNotesUnreadable"/>.</summary>
    public const string Unreadable = "The list of assumptions could not be read, so it is not shown.";
}

/// <summary>Plain English messages of the intake warnings.</summary>
public static class IntakeWarningMessages
{
    /// <summary>Message of <see cref="RunWarningCodes.IntakeFailed"/>.</summary>
    public const string Failed = "Questions could not be prepared for this order, so writing started with your request as written.";

    /// <summary>Message of <see cref="RunWarningCodes.IntakeUnanswered"/>.</summary>
    public const string Unanswered = "There were no answers in time, so writing started with the assumptions shown.";
}

/// <summary>Values of <see cref="RunWarningEntity.Source"/>: the part of the system that reported the warning.</summary>
public static class RunWarningSources
{
    /// <summary>The grounding step of the direct path (looking things up on the web).</summary>
    public const string Grounding = "grounding";

    /// <summary>The review step (reviewers checking a draft).</summary>
    public const string Review = "review";

    /// <summary>The model matrix that assigns models to actors when a run is submitted.</summary>
    public const string ModelAssignment = "model-assignment";

    /// <summary>The LLM client (gateway failover).</summary>
    public const string Llm = "llm";

    /// <summary>The orchestrator that stores the result of a run.</summary>
    public const string Run = "run";

    /// <summary>The intake that clarifies an order before its first run.</summary>
    public const string Intake = "intake";
}
