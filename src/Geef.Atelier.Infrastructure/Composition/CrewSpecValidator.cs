using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Deterministic, non-LLM implementation of <see cref="ICrewSpecValidator"/>.
/// Validates a Crew-Spec JSON by:
/// <list type="bullet">
///   <item>parsing the JSON into a <see cref="CrewSpecArtifact"/>,</item>
///   <item>resolving every <c>reuse</c> reference against the live profile catalog,</item>
///   <item>checking that all required fields are present for inline definitions, and</item>
///   <item>verifying that provider/model combinations exist in the model catalog.</item>
/// </list>
/// </summary>
internal sealed class CrewSpecValidator(
    ICrewService crewService,
    IModelCatalog modelCatalog,
    IGroundingProviderFactory groundingProviderFactory,
    IToolDefinitionRepository toolRepository,
    ILlmClientResolver llmClientResolver) : ICrewSpecValidator
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<CrewSpecValidationIssue>> ValidateAsync(
        string specJson,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<CrewSpecValidationIssue>();

        // Step 1 – parse
        CrewSpecArtifact? spec;
        try
        {
            spec = CrewSpecParser.Parse(specJson);
        }
        catch
        {
            spec = null;
        }

        if (spec is null)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "spec_json",
                Message:    "Invalid JSON or missing required fields (mode not recognised).",
                IsCritical: true));
            return issues;
        }

        // Step 2 – ExistingTemplate mode
        if (spec.Mode == CrewSpecMode.ExistingTemplate)
        {
            if (string.IsNullOrWhiteSpace(spec.ExistingTemplateName))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      "existing_template_name",
                    Message:    "ExistingTemplate mode requires a non-empty existing_template_name.",
                    IsCritical: true));
            }
            else
            {
                var template = await crewService
                    .GetCrewTemplateAsync(spec.ExistingTemplateName, cancellationToken);

                if (template is null)
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      "existing_template_name",
                        Message:    $"Crew template '{spec.ExistingTemplateName}' was not found in the catalog.",
                        IsCritical: true));
            }

            return issues;
        }

        // Step 3 – Composed mode: executor.
        // The executor MUST be a new, inline, task-specialized profile — never reused. A generic
        // executor produces off-target output, so reuse is a hard (deterministic) rejection.
        if (spec.Executor is null)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "executor",
                Message:    "Executor is required.",
                IsCritical: true));
        }
        else if (!string.IsNullOrWhiteSpace(spec.Executor.Reuse))
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "executor.reuse",
                Message:    "The executor must be a new, inline, task-specialized profile — reusing an " +
                            $"executor (here '{spec.Executor.Reuse}') is not allowed. Define an inline executor " +
                            "with name, provider, model, max_tokens and a task-specific system_prompt.",
                IsCritical: true));
        }
        else
        {
            await ValidateProfileRefAsync(
                spec.Executor, "executor",
                name => crewService.GetExecutorProfileAsync(name, cancellationToken),
                issues, cancellationToken);
        }

        // Step 4 – reviewers
        if (spec.Reviewers.Count == 0)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "reviewers",
                Message:    "At least one reviewer is required.",
                IsCritical: true));
        }
        else
        {
            for (var i = 0; i < spec.Reviewers.Count; i++)
            {
                await ValidateProfileRefAsync(
                    spec.Reviewers[i], $"reviewers[{i}]",
                    name => crewService.GetReviewerProfileAsync(name, cancellationToken),
                    issues, cancellationToken);
            }
        }

        // Step 5 – finalizers
        if (spec.Finalizers.Count == 0)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "finalizers",
                Message:    "At least one finalizer is required.",
                IsCritical: true));
        }
        else
        {
            for (var i = 0; i < spec.Finalizers.Count; i++)
            {
                // Only LLM-based finalizers (Transform) need a provider/model.
                // Deterministic finalizer types (FileExport, MetadataEnrich, ExternalSink,
                // CrewMaterialize, LearningExtract, LearningPublish) have no LLM call.
                // For those, skip both the required-field check and the availability check.
                var finalizerType = spec.Finalizers[i].FinalizerType;
                var isLlmFinalizer = string.Equals(finalizerType, "Transform", StringComparison.OrdinalIgnoreCase);

                await ValidateProfileRefAsync(
                    spec.Finalizers[i], $"finalizers[{i}]",
                    name => crewService.GetFinalizerProfileAsync(name, cancellationToken),
                    issues, cancellationToken,
                    skipModelCheck: !isLlmFinalizer,
                    skipProviderModelRequired: !isLlmFinalizer,
                    skipSystemPromptRequired: !isLlmFinalizer);
            }
        }

        // Step 6 – advisors (optional, but reuse references must resolve)
        for (var i = 0; i < spec.Advisors.Count; i++)
        {
            await ValidateProfileRefAsync(
                spec.Advisors[i], $"advisors[{i}]",
                name => crewService.GetAdvisorProfileAsync(name, cancellationToken),
                issues, cancellationToken);
        }

        // Step 7 – grounding providers (optional, but reuse references must resolve)
        for (var i = 0; i < spec.GroundingProviders.Count; i++)
        {
            var groundingRef = spec.GroundingProviders[i];

            // Grounding providers are config-driven (provider_type + settings), not LLM-based.
            // They never need system_prompt, provider, or model fields.
            await ValidateProfileRefAsync(
                groundingRef, $"grounding_providers[{i}]",
                name => crewService.GetGroundingProviderProfileAsync(name, cancellationToken),
                issues, cancellationToken,
                skipModelCheck: true,
                skipProviderModelRequired: true,
                skipSystemPromptRequired: true);

            // For inline definitions the provider_type must be a registered discriminator; otherwise
            // the materialized crew fails at its Grounding phase ("No grounding provider is registered
            // for type ..."). Reuse references are skipped — they resolve to an existing valid profile.
            if (string.IsNullOrWhiteSpace(groundingRef.Reuse))
            {
                if (string.IsNullOrWhiteSpace(groundingRef.ProviderType))
                {
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"grounding_providers[{i}].provider_type",
                        Message:    "Inline grounding provider is missing a required 'provider_type' field.",
                        IsCritical: true));
                }
                else if (!groundingProviderFactory.IsRegistered(groundingRef.ProviderType))
                {
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"grounding_providers[{i}].provider_type",
                        Message:    $"Grounding provider_type '{groundingRef.ProviderType}' is not registered. " +
                                    $"Valid types: {string.Join(", ", groundingProviderFactory.RegisteredTypes.OrderBy(t => t))}.",
                        IsCritical: true));
                }
            }
        }

        // Step 8 – tool bindings
        await ValidateToolBindingsAsync(spec, issues, cancellationToken);

        return issues;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates all <c>tool_names</c> bindings across every actor in the spec:
    /// <list type="bullet">
    ///   <item>8a — provider supports agentic tool use,</item>
    ///   <item>8b — tool name exists in the tool catalogue,</item>
    ///   <item>8c — tool is ReadOnly (Mutating tools blocked in Phase B).</item>
    /// </list>
    /// </summary>
    private async Task ValidateToolBindingsAsync(
        CrewSpecArtifact spec,
        List<CrewSpecValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        // Collect all (actorPath, providerName, toolNames) tuples from the spec.
        // Grounding providers are Push-only; they never call an LLM with agentic tools.
        var actorBindings = new List<(string Path, string? Provider, IReadOnlyList<string>? ToolNames)>();

        if (spec.Executor is not null)
            actorBindings.Add(("executor", spec.Executor.Provider, spec.Executor.ToolNames));

        for (var i = 0; i < spec.Reviewers.Count; i++)
            actorBindings.Add(($"reviewers[{i}]", spec.Reviewers[i].Provider, spec.Reviewers[i].ToolNames));

        for (var i = 0; i < spec.Advisors.Count; i++)
            actorBindings.Add(($"advisors[{i}]", spec.Advisors[i].Provider, spec.Advisors[i].ToolNames));

        for (var i = 0; i < spec.Finalizers.Count; i++)
            actorBindings.Add(($"finalizers[{i}]", spec.Finalizers[i].Provider, spec.Finalizers[i].ToolNames));

        // Cache all tool lookups in one batch to avoid N+1.
        var allNames = actorBindings
            .Where(a => a.ToolNames is { Count: > 0 })
            .SelectMany(a => a.ToolNames!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toolCache = new Dictionary<string, ToolDefinition?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in allNames)
            toolCache[name] = await toolRepository.GetByNameAsync(name, cancellationToken);

        foreach (var (path, provider, toolNames) in actorBindings)
        {
            if (toolNames is not { Count: > 0 }) continue;

            // Check 8a – provider capability (skip if provider is unknown/empty — other steps cover that)
            if (!string.IsNullOrWhiteSpace(provider) && !llmClientResolver.SupportsAgenticTools(provider))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{path}.provider",
                    Message:    $"Provider '{provider}' does not support agentic tool-use but this actor has tool_names bound. " +
                                "Remove the tool bindings or choose a provider that supports agentic tools.",
                    IsCritical: true));
            }

            foreach (var toolName in toolNames)
            {
                // Check 8b – tool exists in catalog
                if (!toolCache.TryGetValue(toolName, out var tool) || tool is null)
                {
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"{path}.tool_names",
                        Message:    $"Tool '{toolName}' is not registered in the tool catalogue.",
                        IsCritical: true));
                    continue;
                }

                // Check 8c – Mutating tools blocked in Phase B
                if (tool.AccessClass == ToolAccessClass.Mutating)
                {
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"{path}.tool_names",
                        Message:    $"Tool '{toolName}' has AccessClass=Mutating. Mutating tools are not permitted in Phase B. " +
                                    "Use only ReadOnly tools, or enable Mutating access via the Phase C opt-in.",
                        IsCritical: true));
                }
            }
        }
    }

    /// <summary>
    /// Validates a single profile reference: resolves <c>reuse</c> names against the catalog, or
    /// checks required inline fields. Optionally verifies the provider/model combination.
    /// </summary>
    private async Task ValidateProfileRefAsync<TProfile>(
        CrewPartSpec profileRef,
        string fieldPath,
        Func<string, Task<TProfile?>> catalogLookup,
        List<CrewSpecValidationIssue> issues,
        CancellationToken cancellationToken,
        bool skipModelCheck = false,
        bool skipProviderModelRequired = false,
        bool skipSystemPromptRequired = false)
        where TProfile : class
    {
        if (!string.IsNullOrWhiteSpace(profileRef.Reuse))
        {
            // Reuse reference — verify catalog entry exists.
            var found = await catalogLookup(profileRef.Reuse);
            if (found is null)
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{fieldPath}.reuse",
                    Message:    $"Profile '{profileRef.Reuse}' was not found in the catalog.",
                    IsCritical: true));
            return;
        }

        // Inline definition — check required fields.
        if (string.IsNullOrWhiteSpace(profileRef.Name))
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{fieldPath}.name",
                Message:    "Inline profile is missing a required 'name' field.",
                IsCritical: false));

        if (!skipSystemPromptRequired && string.IsNullOrWhiteSpace(profileRef.SystemPrompt))
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{fieldPath}.system_prompt",
                Message:    "Inline profile is missing a required 'system_prompt' field.",
                IsCritical: false));

        if (!skipProviderModelRequired)
        {
            if (string.IsNullOrWhiteSpace(profileRef.Provider))
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{fieldPath}.provider",
                    Message:    "Inline profile is missing a required 'provider' field.",
                    IsCritical: false));

            if (string.IsNullOrWhiteSpace(profileRef.Model))
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{fieldPath}.model",
                    Message:    "Inline profile is missing a required 'model' field.",
                    IsCritical: false));
        }

        // Provider/model availability check (only when all fields present and not skipped)
        if (!skipModelCheck
            && !string.IsNullOrWhiteSpace(profileRef.Provider)
            && !string.IsNullOrWhiteSpace(profileRef.Model))
        {
            try
            {
                var models = await modelCatalog.ListModelsAsync(profileRef.Provider, cancellationToken);
                var available = models.Any(m =>
                    string.Equals(m.Id, profileRef.Model, StringComparison.OrdinalIgnoreCase));

                if (!available)
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"{fieldPath}.model",
                        Message:    $"Model '{profileRef.Model}' is not currently available from provider '{profileRef.Provider}'.",
                        IsCritical: false));
            }
            catch
            {
                // Network or provider issue — skip availability check gracefully, same as TemplateStudioService.
            }
        }
    }
}
