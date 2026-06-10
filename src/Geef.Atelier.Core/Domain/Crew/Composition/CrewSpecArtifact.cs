namespace Geef.Atelier.Core.Domain.Crew.Composition;

/// <summary>Mode of the proposed crew composition.</summary>
public enum CrewSpecMode
{
    /// <summary>Reuse an existing crew template by name.</summary>
    ExistingTemplate,

    /// <summary>Compose a new crew from a mix of existing and new profiles.</summary>
    Composed,

    /// <summary>Define a fully new crew from scratch.</summary>
    New
}

/// <summary>
/// A part that either reuses an existing profile by name or defines one inline.
/// </summary>
public sealed record CrewPartSpec
{
    /// <summary>If set, this is a reuse-reference to an existing profile by name.</summary>
    public string? Reuse { get; init; }

    // Inline fields (populated when Reuse is null):

    /// <summary>Unique machine-readable name for the new profile.</summary>
    public string? Name { get; init; }

    /// <summary>Human-readable display name for the new profile.</summary>
    public string? DisplayName { get; init; }

    /// <summary>System prompt used by the LLM for this profile.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>LLM provider identifier (e.g. "openai", "anthropic").</summary>
    public string? Provider { get; init; }

    /// <summary>LLM model identifier.</summary>
    public string? Model { get; init; }

    /// <summary>Maximum tokens for the LLM call.</summary>
    public int? MaxTokens { get; init; }

    // Reviewer-only:

    /// <summary>Review priority (lower = higher priority). Reviewer-only.</summary>
    public int? Priority { get; init; }

    // Advisor-only:

    /// <summary>Advisor operating mode. One of "Strategic", "Critical", "DevilsAdvocate", "DomainExpert". Advisor-only.</summary>
    public string? AdvisorMode { get; init; }

    /// <summary>When the advisor is triggered. One of "BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure". Advisor-only.</summary>
    public string? AdvisorTrigger { get; init; }

    // GroundingProvider-only:

    /// <summary>Type of grounding provider (e.g. "tavily", "static-context", "crew-catalog"). GroundingProvider-only.</summary>
    public string? ProviderType { get; init; }

    // Finalizer-only:

    /// <summary>Finalizer execution type (e.g. "FileExport", "Transform"). Finalizer-only.</summary>
    public string? FinalizerType { get; init; }

    /// <summary>
    /// Optional list of tool names (from the tool catalogue) to bind to this actor's profile.
    /// Applies to executor, reviewer, advisor, and Transform-type finalizer parts.
    /// </summary>
    public IReadOnlyList<string>? ToolNames { get; init; }
}

/// <summary>
/// Structured artifact produced by the composition meta-LLM describing a complete crew configuration.
/// </summary>
public sealed record CrewSpecArtifact
{
    /// <summary>Whether this spec reuses an existing template, composes from existing parts, or defines everything new.</summary>
    public CrewSpecMode Mode { get; init; }

    /// <summary>Domain hint for the crew (e.g. "legal", "academic", "general").</summary>
    public string Domain { get; init; } = "general";

    /// <summary>Meta-LLM rationale explaining why this crew configuration was chosen.</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Name of the existing template to reuse. Only populated when <see cref="Mode"/> is <see cref="CrewSpecMode.ExistingTemplate"/>.</summary>
    public string? ExistingTemplateName { get; init; }

    /// <summary>Executor part spec. Populated when <see cref="Mode"/> is <see cref="CrewSpecMode.Composed"/> or <see cref="CrewSpecMode.New"/>.</summary>
    public CrewPartSpec? Executor { get; init; }

    /// <summary>Reviewer part specs.</summary>
    public IReadOnlyList<CrewPartSpec> Reviewers { get; init; } = [];

    /// <summary>Advisor part specs.</summary>
    public IReadOnlyList<CrewPartSpec> Advisors { get; init; } = [];

    /// <summary>Grounding provider part specs.</summary>
    public IReadOnlyList<CrewPartSpec> GroundingProviders { get; init; } = [];

    /// <summary>Finalizer part specs.</summary>
    public IReadOnlyList<CrewPartSpec> Finalizers { get; init; } = [];

    /// <summary>How reviewers evaluate each iteration. Defaults to "Parallel".</summary>
    public string EvaluationStrategy { get; init; } = "Parallel";

    /// <summary>Optional override for the maximum number of review iterations.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Whether to abort the run immediately when a critical finding is raised.</summary>
    public bool? AbortOnCritical { get; init; }
}
