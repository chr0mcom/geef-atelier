using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Application.Crew;

/// <summary>
/// Builds fully-dereferenced <see cref="CrewSnapshot"/> instances from templates or inline specs.
/// Profile lookups are provided via callbacks so the builder remains testable without EF dependencies.
/// </summary>
public static class CrewSnapshotBuilder
{
    /// <summary>Builds a snapshot from a named crew template, resolving all referenced profiles.</summary>
    public static async Task<CrewSnapshot> BuildAsync(
        CrewTemplate template,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        Func<string, CancellationToken, Task<AdvisorProfile?>> advisorLookup,
        Func<string, CancellationToken, Task<GroundingProviderProfile?>> groundingLookup,
        Func<string, CancellationToken, Task<FinalizerProfile?>>? finalizerLookup = null,
        CancellationToken cancellationToken = default)
    {
        var executor = await executorLookup(template.ExecutorProfileName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Executor profile '{template.ExecutorProfileName}' referenced by template '{template.Name}' not found.");

        var reviewers = await ResolveReviewersAsync(template.ReviewerProfileNames, reviewerLookup, cancellationToken);
        var advisors = await ResolveAdvisorsAsync(template.AdvisorProfileNames, advisorLookup, cancellationToken);
        var groundingProviders = await ResolveGroundingProvidersAsync(template.GroundingProviderNames, groundingLookup, cancellationToken);
        var finalizers = finalizerLookup is not null
            ? await ResolveFinalizersAsync(template.FinalizerProfileNames, finalizerLookup, cancellationToken)
            : null;

        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: template.Name,
            Executor: executor,
            Reviewers: reviewers,
            EvaluationStrategy: template.EvaluationStrategy,
            ConvergenceOverride: template.ConvergenceOverride,
            Advisors: advisors,
            GroundingProviders: groundingProviders,
            Finalizers: finalizers,
            RunFinalizersOnMaxAttempts: template.RunFinalizersOnMaxAttempts,
            CrewDomain: template.Domain ?? Core.Domain.Crew.Models.ModelDomain.General);
    }

    /// <summary>Builds a snapshot from an inline crew spec (no template name), resolving all referenced profiles.</summary>
    public static async Task<CrewSnapshot> BuildAsync(
        CrewSpec spec,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        Func<string, CancellationToken, Task<AdvisorProfile?>> advisorLookup,
        Func<string, CancellationToken, Task<GroundingProviderProfile?>> groundingLookup,
        Func<string, CancellationToken, Task<FinalizerProfile?>>? finalizerLookup = null,
        CancellationToken cancellationToken = default)
    {
        var executor = await executorLookup(spec.ExecutorProfileName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Executor profile '{spec.ExecutorProfileName}' referenced by inline crew spec not found.");

        var reviewers = await ResolveReviewersAsync(spec.ReviewerProfileNames, reviewerLookup, cancellationToken);
        var advisors = await ResolveAdvisorsAsync(spec.AdvisorProfileNames, advisorLookup, cancellationToken);
        var groundingProviders = await ResolveGroundingProvidersAsync(spec.GroundingProviderNames, groundingLookup, cancellationToken);
        var finalizers = finalizerLookup is not null
            ? await ResolveFinalizersAsync(spec.FinalizerProfileNames, finalizerLookup, cancellationToken)
            : null;

        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: executor,
            Reviewers: reviewers,
            EvaluationStrategy: spec.EvaluationStrategy,
            ConvergenceOverride: spec.ConvergenceOverride,
            Advisors: advisors,
            GroundingProviders: groundingProviders,
            Finalizers: finalizers,
            CrewDomain: spec.Domain ?? Core.Domain.Crew.Models.ModelDomain.General);
    }

    /// <summary>
    /// Composes each actor's generic role prompt with its bound specialization packs (in order),
    /// writing the composed prompt into the embedded profile's <c>SystemPrompt</c> so the pipeline
    /// factory and actors need no changes, and recording the composition (role + composed + provenance)
    /// in <see cref="CrewSnapshot.PromptCompositions"/> for reproducibility and the audit UI.
    /// Packs whose <c>ApplicableActorTypes</c> do not match the actor are skipped (logged). Returns the
    /// snapshot unchanged when <paramref name="actorPackBindings"/> is empty.
    /// </summary>
    public static async Task<CrewSnapshot> ApplyPacksAsync(
        CrewSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<string>> actorPackBindings,
        Func<string, CancellationToken, Task<SpecializationPack?>> packLookup,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (actorPackBindings.Count == 0)
            return snapshot;

        var compositions = new List<ActorPromptComposition>();

        // Executor
        var (composedExec, execComp) = await ComposeActorAsync(
            ActorTypeKeys.Executor, snapshot.Executor.Name, snapshot.Executor.SystemPrompt,
            PackActorType.Executor, actorPackBindings, packLookup, logger, cancellationToken);
        var executor = execComp is null ? snapshot.Executor : snapshot.Executor with { SystemPrompt = composedExec };
        if (execComp is not null) compositions.Add(execComp);

        // Reviewers
        var reviewers = new List<ReviewerProfile>(snapshot.Reviewers.Count);
        foreach (var r in snapshot.Reviewers)
        {
            var (composed, comp) = await ComposeActorAsync(
                ActorTypeKeys.Reviewer, r.Name, r.SystemPrompt,
                PackActorType.Reviewer, actorPackBindings, packLookup, logger, cancellationToken);
            reviewers.Add(comp is null ? r : r with { SystemPrompt = composed });
            if (comp is not null) compositions.Add(comp);
        }

        // Advisors
        var advisors = new List<AdvisorProfile>(snapshot.Advisors.Count);
        foreach (var a in snapshot.Advisors)
        {
            var (composed, comp) = await ComposeActorAsync(
                ActorTypeKeys.Advisor, a.Name, a.SystemPrompt,
                PackActorType.Advisor, actorPackBindings, packLookup, logger, cancellationToken);
            advisors.Add(comp is null ? a : a with { SystemPrompt = composed });
            if (comp is not null) compositions.Add(comp);
        }

        return snapshot with
        {
            Executor = executor,
            Reviewers = reviewers,
            Advisors = advisors,
            PromptCompositions = compositions.Count > 0 ? compositions : null
        };
    }

    /// <summary>
    /// Resolves the ordered packs bound to one actor, composes them onto the role prompt, and returns
    /// the composed prompt plus its provenance record. Returns <c>(rolePrompt, null)</c> when the actor
    /// has no (effective) bound packs.
    /// </summary>
    private static async Task<(string Composed, ActorPromptComposition? Comp)> ComposeActorAsync(
        string actorTypeKey,
        string actorName,
        string rolePrompt,
        PackActorType actorType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings,
        Func<string, CancellationToken, Task<SpecializationPack?>> packLookup,
        ILogger? logger,
        CancellationToken ct)
    {
        if (!bindings.TryGetValue(ActorTypeKeys.BindingKey(actorTypeKey, actorName), out var packNames)
            || packNames.Count == 0)
            return (rolePrompt, null);

        var packs = new List<SpecializationPack>(packNames.Count);
        foreach (var name in packNames)
        {
            var pack = await packLookup(name, ct);
            if (pack is null)
            {
                logger?.LogWarning("Pack '{Pack}' bound to actor '{Actor}' not found — skipped.", name, actorName);
                continue;
            }
            if (!pack.ApplicableActorTypes.AppliesTo(actorType))
            {
                logger?.LogWarning(
                    "Pack '{Pack}' is not applicable to actor type {Type} (actor '{Actor}') — skipped.",
                    name, actorType, actorName);
                continue;
            }
            packs.Add(pack);
        }

        if (packs.Count == 0)
            return (rolePrompt, null);

        var composed = PromptComposer.Compose(rolePrompt, packs);
        var provenance = packs
            .Select((p, i) => new PackProvenance(p.Name, p.DisplayName, p.Scope, i))
            .ToList();
        return (composed, new ActorPromptComposition(actorTypeKey, actorName, rolePrompt, composed, provenance));
    }

    private static async Task<IReadOnlyList<ReviewerProfile>> ResolveReviewersAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        CancellationToken cancellationToken)
    {
        var reviewers = new List<ReviewerProfile>(names.Count);
        foreach (var name in names)
        {
            var reviewer = await reviewerLookup(name, cancellationToken)
                ?? throw new InvalidOperationException($"Reviewer profile '{name}' not found.");
            reviewers.Add(reviewer);
        }
        return reviewers;
    }

    private static async Task<IReadOnlyList<AdvisorProfile>> ResolveAdvisorsAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<AdvisorProfile?>> advisorLookup,
        CancellationToken cancellationToken)
    {
        var result = new List<AdvisorProfile>(names.Count);
        foreach (var name in names)
        {
            var profile = await advisorLookup(name, cancellationToken);
            if (profile is not null) result.Add(profile);
            // Silently skip missing advisors (profile deleted after template creation)
        }
        return result;
    }

    private static async Task<IReadOnlyList<GroundingProviderProfile>> ResolveGroundingProvidersAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<GroundingProviderProfile?>> groundingLookup,
        CancellationToken cancellationToken)
    {
        var result = new List<GroundingProviderProfile>(names.Count);
        foreach (var name in names)
        {
            var profile = await groundingLookup(name, cancellationToken);
            if (profile is not null) result.Add(profile);
            // Silently skip missing providers (profile deleted after template creation)
        }
        return result;
    }

    private static async Task<IReadOnlyList<FinalizerProfile>> ResolveFinalizersAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<FinalizerProfile?>> finalizerLookup,
        CancellationToken cancellationToken)
    {
        var result = new List<FinalizerProfile>(names.Count);
        foreach (var name in names)
        {
            var profile = await finalizerLookup(name, cancellationToken);
            if (profile is not null) result.Add(profile);
            // Silently skip missing finalizers (profile deleted after template creation)
        }
        return result;
    }

}
