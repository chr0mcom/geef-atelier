namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>Stable machine-readable codes of <see cref="RunWarningEntity"/> rows, exposed to MCP clients.</summary>
public static class RunWarningCodes
{
    public const string GroundingProviderSkipped = "grounding.provider_skipped";
    public const string GroundingToolFailed = "grounding.tool_failed";
    public const string ReviewerFailed = "review.reviewer_failed";
    public const string ModelAssignment = "model.assignment";
    public const string LlmFailover = "llm.failover";
    public const string FinalizerSkipped = "finalizer.skipped";
    public const string FinalizerFailed = "finalizer.failed";
    public const string FinalizersNotRun = "finalizer.not_run";
}

/// <summary>Values of <see cref="RunWarningEntity.Source"/>: the part of the system that reported the warning.</summary>
public static class RunWarningSources
{
    public const string Grounding = "grounding";
    public const string Review = "review";
    public const string ModelAssignment = "model-assignment";
    public const string Llm = "llm";
    public const string Finalizer = "finalizer";
}
