namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>Well-known actor-type keys used to address pack bindings and compositions.</summary>
public static class ActorTypeKeys
{
    public const string Executor = "executor";
    public const string Reviewer = "reviewer";
    public const string Advisor  = "advisor";

    /// <summary>Builds the binding-dictionary key <c>"&lt;actorType&gt;:&lt;profileName&gt;"</c>.</summary>
    public static string BindingKey(string actorType, string profileName) => $"{actorType}:{profileName}";
}

/// <summary>Provenance of one pack composed into an actor's effective prompt.</summary>
/// <param name="Name">Pack name.</param>
/// <param name="DisplayName">Pack display name.</param>
/// <param name="Scope">Pack scope.</param>
/// <param name="Order">Zero-based position in the composition order.</param>
public sealed record PackProvenance(string Name, string DisplayName, PackScope Scope, int Order);

/// <summary>
/// Frozen record of how a single actor's effective system prompt was composed for a run: the generic
/// role prompt, the packs applied (with order), and the resulting composed string actually used.
/// Stored in the <see cref="CrewSnapshot"/> for reproducibility and surfaced in the audit UI.
/// </summary>
/// <param name="ActorType">One of <see cref="ActorTypeKeys"/> (executor / reviewer / advisor).</param>
/// <param name="ActorName">The actor profile name.</param>
/// <param name="RolePrompt">The generic role prompt before composition.</param>
/// <param name="ComposedPrompt">The effective prompt actually used at runtime.</param>
/// <param name="Packs">The packs composed in, in order.</param>
public sealed record ActorPromptComposition(
    string ActorType,
    string ActorName,
    string RolePrompt,
    string ComposedPrompt,
    IReadOnlyList<PackProvenance> Packs);
