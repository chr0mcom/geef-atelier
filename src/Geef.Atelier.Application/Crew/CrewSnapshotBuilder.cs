using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
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
            RunFinalizersOnMaxAttempts: template.RunFinalizersOnMaxAttempts);
    }

    /// <summary>
    /// Builds a snapshot from a named crew template, resolving all profiles and embedding
    /// fully-dereferenced tool definitions for every actor that declares <c>ToolNames</c>.
    /// </summary>
    public static async Task<CrewSnapshot> BuildWithToolsAsync(
        CrewTemplate template,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        Func<string, CancellationToken, Task<AdvisorProfile?>> advisorLookup,
        Func<string, CancellationToken, Task<GroundingProviderProfile?>> groundingLookup,
        Func<string, CancellationToken, Task<FinalizerProfile?>>? finalizerLookup,
        Func<string, CancellationToken, Task<ToolDefinition?>> toolLookup,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var base_ = await BuildAsync(template, executorLookup, reviewerLookup, advisorLookup,
            groundingLookup, finalizerLookup, cancellationToken);

        var toolBindings = await ResolveToolBindingsAsync(base_, toolLookup, logger, cancellationToken);
        return base_ with { ToolBindings = toolBindings };
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
            Finalizers: finalizers);
    }

    /// <summary>
    /// Builds a snapshot from an inline crew spec, resolving all profiles and embedding
    /// fully-dereferenced tool definitions for every actor that declares <c>ToolNames</c>.
    /// </summary>
    public static async Task<CrewSnapshot> BuildWithToolsAsync(
        CrewSpec spec,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        Func<string, CancellationToken, Task<AdvisorProfile?>> advisorLookup,
        Func<string, CancellationToken, Task<GroundingProviderProfile?>> groundingLookup,
        Func<string, CancellationToken, Task<FinalizerProfile?>>? finalizerLookup,
        Func<string, CancellationToken, Task<ToolDefinition?>> toolLookup,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var base_ = await BuildAsync(spec, executorLookup, reviewerLookup, advisorLookup,
            groundingLookup, finalizerLookup, cancellationToken);

        var toolBindings = await ResolveToolBindingsAsync(base_, toolLookup, logger, cancellationToken);
        return base_ with { ToolBindings = toolBindings };
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

    /// <summary>
    /// Builds the <see cref="CrewSnapshot.ToolBindings"/> dictionary by resolving every tool name
    /// declared across all actors (executor, reviewers, advisors, finalizers).
    /// Returns <c>null</c> when no actor declares any tool names.
    /// Missing tools are logged as warnings and silently skipped.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>>?> ResolveToolBindingsAsync(
        CrewSnapshot snapshot,
        Func<string, CancellationToken, Task<ToolDefinition?>> toolLookup,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var bindings = new Dictionary<string, IReadOnlyList<ToolDefinition>>(StringComparer.Ordinal);

        await AddActorToolsAsync(
            snapshot.Executor.Name,
            snapshot.Executor.ToolNames,
            toolLookup, bindings, logger, cancellationToken);

        foreach (var reviewer in snapshot.Reviewers)
            await AddActorToolsAsync(reviewer.Name, reviewer.ToolNames, toolLookup, bindings, logger, cancellationToken);

        foreach (var advisor in snapshot.Advisors)
            await AddActorToolsAsync(advisor.Name, advisor.ToolNames, toolLookup, bindings, logger, cancellationToken);

        if (snapshot.Finalizers is not null)
            foreach (var finalizer in snapshot.Finalizers)
                await AddActorToolsAsync(finalizer.Name, finalizer.ToolNames, toolLookup, bindings, logger, cancellationToken);

        return bindings.Count > 0 ? bindings : null;
    }

    private static async Task AddActorToolsAsync(
        string actorName,
        IReadOnlyList<string>? toolNames,
        Func<string, CancellationToken, Task<ToolDefinition?>> toolLookup,
        Dictionary<string, IReadOnlyList<ToolDefinition>> bindings,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (toolNames is not { Count: > 0 })
            return;

        var resolved = new List<ToolDefinition>(toolNames.Count);
        foreach (var toolName in toolNames)
        {
            var tool = await toolLookup(toolName, cancellationToken);
            if (tool is not null)
            {
                resolved.Add(tool);
            }
            else
            {
                logger?.LogWarning(
                    "Tool '{ToolName}' declared by actor '{ActorName}' not found in catalogue — skipped in snapshot.",
                    toolName, actorName);
            }
        }

        if (resolved.Count > 0)
            bindings[actorName] = resolved;
    }
}
