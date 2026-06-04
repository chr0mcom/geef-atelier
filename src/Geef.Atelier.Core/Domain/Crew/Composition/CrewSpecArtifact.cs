namespace Geef.Atelier.Core.Domain.Crew.Composition;

/// <summary>
/// Defines the composition mode for a generated Crew-Spec artifact.
/// </summary>
public enum CrewSpecMode
{
    /// <summary>The spec references an existing named crew template by name.</summary>
    ExistingTemplate,

    /// <summary>The spec defines a fully composed crew inline.</summary>
    Composed,
}

/// <summary>
/// Describes an inline or reuse-based profile reference within a Crew-Spec artifact.
/// Exactly one of <see cref="Reuse"/> or the inline fields (<see cref="Name"/>, <see cref="SystemPrompt"/>,
/// <see cref="Provider"/>, <see cref="Model"/>) should be populated.
/// </summary>
public sealed record CrewSpecProfileRef
{
    /// <summary>Name of an existing profile to reuse from the catalog. Mutually exclusive with inline fields.</summary>
    public string? Reuse { get; init; }

    /// <summary>Inline profile name. Required when <see cref="Reuse"/> is null.</summary>
    public string? Name { get; init; }

    /// <summary>System prompt for the inline profile.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Provider identifier for the inline profile (e.g. <c>"openrouter"</c>).</summary>
    public string? Provider { get; init; }

    /// <summary>Model identifier for the inline profile (e.g. <c>"gpt-4o"</c>).</summary>
    public string? Model { get; init; }
}

/// <summary>
/// The structured domain model parsed from a Crew-Spec artifact JSON.
/// Produced by <c>CrewSpecParser.Parse</c> and consumed by <c>ICrewSpecValidator</c>.
/// </summary>
public sealed record CrewSpecArtifact
{
    /// <summary>Composition mode of this spec.</summary>
    public required CrewSpecMode Mode { get; init; }

    /// <summary>
    /// When <see cref="Mode"/> is <see cref="CrewSpecMode.ExistingTemplate"/>, the name of the
    /// referenced crew template. <see langword="null"/> in <see cref="CrewSpecMode.Composed"/> mode.
    /// </summary>
    public string? ExistingTemplateName { get; init; }

    /// <summary>Executor reference. Required in <see cref="CrewSpecMode.Composed"/> mode.</summary>
    public CrewSpecProfileRef? Executor { get; init; }

    /// <summary>Reviewer references. Must be non-empty in <see cref="CrewSpecMode.Composed"/> mode.</summary>
    public IReadOnlyList<CrewSpecProfileRef> Reviewers { get; init; } = [];

    /// <summary>Finalizer references. Must be non-empty in <see cref="CrewSpecMode.Composed"/> mode.</summary>
    public IReadOnlyList<CrewSpecProfileRef> Finalizers { get; init; } = [];

    /// <summary>Advisor references. May be empty.</summary>
    public IReadOnlyList<CrewSpecProfileRef> Advisors { get; init; } = [];

    /// <summary>Grounding-provider references. May be empty.</summary>
    public IReadOnlyList<CrewSpecProfileRef> GroundingProviders { get; init; } = [];
}
