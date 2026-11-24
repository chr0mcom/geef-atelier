namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>Stable machine-readable codes of <see cref="RunWarningEntity"/> rows, exposed to MCP clients.</summary>
public static class RunWarningCodes
{
    /// <summary>A grounding source failed (provider error, missing key, unknown type) and was left out.</summary>
    public const string GroundingProviderSkipped = "grounding.provider_skipped";

    /// <summary>A tool-backed grounding source returned an error. Reserved; failures currently use <see cref="GroundingProviderSkipped"/>.</summary>
    public const string GroundingToolFailed = "grounding.tool_failed";

    /// <summary>A reviewer could not complete its check in a round and was left out of the verdict.</summary>
    public const string ReviewerFailed = "review.reviewer_failed";

    /// <summary>The model matrix could not assign the planned model to an actor; another one was used.</summary>
    public const string ModelAssignment = "model.assignment";

    /// <summary>The AI gateway answered from another service than requested (failover); model plurality may not hold.</summary>
    public const string LlmFailover = "llm.failover";

    /// <summary>A finishing step did nothing although it was expected to act, or had no effect.</summary>
    public const string FinalizerSkipped = "finalizer.skipped";

    /// <summary>A finishing step failed, or the results of the finishing steps could not be stored.</summary>
    public const string FinalizerFailed = "finalizer.failed";

    /// <summary>The finishing steps were not run (best-effort result without permission, or no executor available).</summary>
    public const string FinalizersNotRun = "finalizer.not_run";

    /// <summary>The run finished, but its status or final text could not be stored; it was recorded as failed.</summary>
    public const string ResultNotSaved = "run.result_not_saved";
}

/// <summary>Values of <see cref="RunWarningEntity.Source"/>: the part of the system that reported the warning.</summary>
public static class RunWarningSources
{
    /// <summary>The grounding step (web search, knowledge base, attachments and other sources).</summary>
    public const string Grounding = "grounding";

    /// <summary>The review step (reviewers checking a draft).</summary>
    public const string Review = "review";

    /// <summary>The model matrix that assigns models to actors when a run is submitted.</summary>
    public const string ModelAssignment = "model-assignment";

    /// <summary>The LLM client (gateway failover).</summary>
    public const string Llm = "llm";

    /// <summary>The finishing steps (finalizers) after the text is delivered.</summary>
    public const string Finalizer = "finalizer";

    /// <summary>The orchestrator that stores the result of a run.</summary>
    public const string Run = "run";
}
