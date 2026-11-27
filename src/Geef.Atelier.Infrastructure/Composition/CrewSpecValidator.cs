using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
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
///   <item>checking model slots and the CLI-route rule (models come from the model matrix, D-066).</item>
/// </list>
/// </summary>
internal sealed class CrewSpecValidator(
    ICrewService crewService,
    IModelCatalog modelCatalog,
    IGroundingProviderFactory groundingProviderFactory,
    IToolDefinitionRepository toolRepository,
    ISpecializationPackRepository packRepository,
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
                            "with name, model_slot \"Executor\", max_tokens and a task-specific system_prompt.",
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
                var isTransform = spec.Finalizers[i].IsInlineTransform();

                await ValidateProfileRefAsync(
                    spec.Finalizers[i], $"finalizers[{i}]",
                    name => crewService.GetFinalizerProfileAsync(name, cancellationToken),
                    issues, cancellationToken,
                    skipSystemPromptRequired: !isTransform);

                ValidateInlineFinalizerSettings(spec.Finalizers[i], $"finalizers[{i}]", issues);
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
                skipSystemPromptRequired: true);

            // For inline definitions the provider_type must be a registered discriminator; otherwise
            // the materialized crew fails at its Grounding phase ("No grounding provider is registered
            // for type ..."). Reuse references are skipped — they resolve to an existing valid profile.
            if (string.IsNullOrWhiteSpace(groundingRef.Reuse))
            {
                ValidateInlineGroundingSettings(groundingRef, $"grounding_providers[{i}]", issues);

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

        // Step 9 – specialization-pack bindings
        await ValidatePackBindingsAsync(spec, issues, cancellationToken);

        ValidateModelSlots(spec, issues);

        await ValidateModelPluralityAsync(spec, issues, cancellationToken);

        return issues;
    }

    /// <summary>
    /// Step 11 — model slots and CLI route (D-066). Inline LLM parts get their model from the model matrix: an
    /// unparsable or disallowed <c>model_slot</c> is critical, a missing one defaults (warning), and a
    /// Claude/OpenAI binding on a non-CLI provider is reported as being rewritten (warning). A missing or invalid
    /// <c>model_domain</c> is a warning: the crew then runs on the row that <c>CrewSpecArtifact.EffectiveModelDomain</c>
    /// resolves (the free <c>domain</c> only if it is an exact enum name, else General).
    /// </summary>
    private static void ValidateModelSlots(CrewSpecArtifact spec, List<CrewSpecValidationIssue> issues)
    {
        if (EnumNames.ParseExact<ModelDomain>(spec.MatrixDomain) is null)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      "model_domain",
                Message:    spec.MatrixDomain is null
                    ? $"No model_domain given; using '{spec.EffectiveModelDomain()}'. Valid values: {string.Join(", ", Enum.GetNames<ModelDomain>())}."
                    : $"model_domain '{spec.MatrixDomain}' is not valid; using '{spec.EffectiveModelDomain()}'. Valid values: {string.Join(", ", Enum.GetNames<ModelDomain>())}.",
                IsCritical: false));
        }

        var parts = new List<(string Path, CrewPartSpec Part, CrewPartKind Kind)>();
        if (spec.Executor is not null)
            parts.Add(("executor", spec.Executor, CrewPartKind.Executor));
        for (var i = 0; i < spec.Reviewers.Count; i++)
            parts.Add(($"reviewers[{i}]", spec.Reviewers[i], CrewPartKind.Reviewer));
        for (var i = 0; i < spec.Advisors.Count; i++)
            parts.Add(($"advisors[{i}]", spec.Advisors[i], CrewPartKind.Advisor));
        for (var i = 0; i < spec.Finalizers.Count; i++)
        {
            if (spec.Finalizers[i].IsInlineTransform())
                parts.Add(($"finalizers[{i}]", spec.Finalizers[i], CrewPartKind.TransformFinalizer));
        }

        foreach (var (path, part, kind) in parts)
        {
            if (!string.IsNullOrWhiteSpace(part.Reuse))
                continue;

            var allowed = string.Join(", ", kind.AllowedSlots());
            if (string.IsNullOrWhiteSpace(part.ModelSlot))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{path}.model_slot",
                    Message:    $"No model_slot given; defaulting to '{part.DefaultSlot(kind)}'. Valid values: {allowed}.",
                    IsCritical: false));
            }
            else if (!ModelSlotParser.TryParse(part.ModelSlot, out var slot))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{path}.model_slot",
                    Message:    $"Unknown model_slot '{part.ModelSlot}'. Valid values: {allowed}.",
                    IsCritical: true));
            }
            else if (!slot.IsAllowedFor(kind))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{path}.model_slot",
                    Message:    $"model_slot '{slot}' is not valid for this part. Valid values: {allowed}.",
                    IsCritical: true));
            }

            if (!string.IsNullOrWhiteSpace(part.Provider) && !string.IsNullOrWhiteSpace(part.Model)
                && CliRoute.IsNonCliRoute(part.Provider!, part.Model!))
            {
                var target = CliRoute.Normalize(part.Provider!, part.Model!);
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"{path}.provider",
                    Message:    $"'{part.Provider}/{part.Model}' will be rewritten to the subscription CLI route '{target}'; " +
                                "provider and model are assigned by the model matrix anyway.",
                    IsCritical: false));
            }
        }
    }

    /// <summary>
    /// Step 10 — flags reused reviewers with a manual binding that equals the manual binding of a reused executor.
    /// Managed actors (inline parts and profiles with a model slot) are excluded on both sides: the model matrix
    /// assigns them at run start, where a manual reviewer on a managed executor's model is reported as a warning.
    /// Aliases and concrete ids are resolved before comparing.
    /// </summary>
    private async Task ValidateModelPluralityAsync(
        CrewSpecArtifact spec, List<CrewSpecValidationIssue> issues, CancellationToken cancellationToken)
    {
        if (spec.Executor is null)
            return;

        var executor = await EffectiveExecutorBindingAsync(spec, cancellationToken);
        if (executor is null)
            return;

        for (var i = 0; i < spec.Reviewers.Count; i++)
        {
            var reviewerName = spec.Reviewers[i].Reuse;
            if (string.IsNullOrWhiteSpace(reviewerName))
                continue;

            var reviewer = await crewService.GetReviewerProfileAsync(reviewerName, cancellationToken);
            if (reviewer is null || reviewer.ModelSlot is not null
                || string.IsNullOrWhiteSpace(reviewer.Provider) || string.IsNullOrWhiteSpace(reviewer.Model))
                continue;

            var reviewerModel = await ResolveModelSafeAsync(reviewer.Provider, reviewer.Model, cancellationToken);
            if (CliRoute.IsSameModel(reviewerModel, executor.Value.Model))
            {
                issues.Add(new CrewSpecValidationIssue(
                    Field:      $"reviewers[{i}].reuse",
                    Message:    $"Reused reviewer '{reviewerName}' has a manual binding on the executor's model ('{reviewer.Model}'), so the crew reviews itself. "
                                + "Model plurality requires a different model, ideally from another vendor; prefer a reviewer with a model slot.",
                    IsCritical: false));
            }
        }
    }

    /// <summary>Manual binding of a reused executor without model slot; null for inline or managed executors.</summary>
    private async Task<(string Provider, string Model)?> EffectiveExecutorBindingAsync(
        CrewSpecArtifact spec, CancellationToken cancellationToken)
    {
        var part = spec.Executor!;
        if (string.IsNullOrWhiteSpace(part.Reuse))
            return null;

        var profile = await crewService.GetExecutorProfileAsync(part.Reuse!, cancellationToken);
        if (profile is null || profile.ModelSlot is not null
            || string.IsNullOrWhiteSpace(profile.Provider) || string.IsNullOrWhiteSpace(profile.Model))
            return null;
        return (profile.Provider, await ResolveModelSafeAsync(profile.Provider, profile.Model, cancellationToken));
    }

    private async Task<string> ResolveModelSafeAsync(string provider, string model, CancellationToken cancellationToken)
    {
        try
        {
            return await modelCatalog.ResolveModelAsync(provider, model, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return model;
        }
    }

    /// <summary>
    /// Validates pack bindings on every actor: referenced packs either exist (reuse) or are defined in
    /// <c>spec.NewPacks</c>; reused packs are type-compatible and never foreign TaskBound; new packs are
    /// correctly scoped (DomainScoped requires a domain, valid name).
    /// </summary>
    private async Task ValidatePackBindingsAsync(
        CrewSpecArtifact spec,
        List<CrewSpecValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        // Validate new pack definitions first, and index them by name.
        var newPackTypes = new Dictionary<string, IReadOnlyList<PackActorType>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < spec.NewPacks.Count; i++)
        {
            var np = spec.NewPacks[i];
            if (string.IsNullOrWhiteSpace(np.Name) || !SpecializationPack.IsValidName(np.Name))
                issues.Add(new CrewSpecValidationIssue($"packs[{i}].name",
                    "New pack is missing a valid name (lowercase letters, digits, hyphens).", true));
            if (string.IsNullOrWhiteSpace(np.SpecializationText))
                issues.Add(new CrewSpecValidationIssue($"packs[{i}].specialization_text",
                    "New pack is missing required 'specialization_text'.", true));
            if (string.Equals(np.Scope, "DomainScoped", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(np.Domain) && string.IsNullOrWhiteSpace(spec.Domain))
                issues.Add(new CrewSpecValidationIssue($"packs[{i}].domain",
                    "DomainScoped pack requires a 'domain'.", true));
            if (np.Name is not null)
                newPackTypes[np.Name] = ParseNewPackActorTypes(np.ApplicableActorTypes);
        }

        var actorBindings = new List<(string Path, PackActorType Type, IReadOnlyList<string>? Packs)>();
        if (spec.Executor is not null)
            actorBindings.Add(("executor", PackActorType.Executor, spec.Executor.PackNames));
        for (var i = 0; i < spec.Reviewers.Count; i++)
            actorBindings.Add(($"reviewers[{i}]", PackActorType.Reviewer, spec.Reviewers[i].PackNames));
        for (var i = 0; i < spec.Advisors.Count; i++)
            actorBindings.Add(($"advisors[{i}]", PackActorType.Advisor, spec.Advisors[i].PackNames));

        foreach (var (path, actorType, packNames) in actorBindings)
        {
            if (packNames is not { Count: > 0 }) continue;
            foreach (var packName in packNames)
            {
                // New pack defined in this spec?
                if (newPackTypes.TryGetValue(packName, out var newTypes))
                {
                    if (!newTypes.AppliesTo(actorType))
                        issues.Add(new CrewSpecValidationIssue($"{path}.pack_names",
                            $"New pack '{packName}' does not declare actor type '{actorType}'.", true));
                    continue;
                }

                // Otherwise must resolve to an existing reusable pack.
                var pack = await packRepository.GetByNameAsync(packName, cancellationToken);
                if (pack is null)
                {
                    issues.Add(new CrewSpecValidationIssue($"{path}.pack_names",
                        $"Pack '{packName}' is not in the catalogue and is not defined in 'packs'.", true));
                    continue;
                }
                if (pack.Scope == PackScope.TaskBound)
                    issues.Add(new CrewSpecValidationIssue($"{path}.pack_names",
                        $"Pack '{packName}' is TaskBound to another crew and cannot be reused here.", true));
                else if (!pack.ApplicableActorTypes.AppliesTo(actorType))
                    issues.Add(new CrewSpecValidationIssue($"{path}.pack_names",
                        $"Pack '{packName}' is not applicable to actor type '{actorType}'.", true));
            }
        }
    }

    private static IReadOnlyList<PackActorType> ParseNewPackActorTypes(IReadOnlyList<string>? raw)
    {
        if (raw is not { Count: > 0 }) return [PackActorType.Any];
        return raw.Select(s => s?.ToLowerInvariant() switch
        {
            "executor" => PackActorType.Executor,
            "reviewer" => PackActorType.Reviewer,
            "advisor"  => PackActorType.Advisor,
            _          => PackActorType.Any,
        }).Distinct().ToList();
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

        var crewDomain = spec.EffectiveModelDomain();
        string? EffectiveProvider(CrewPartSpec part, CrewPartKind kind) =>
            string.IsNullOrWhiteSpace(part.Reuse)
                ? ModelMatrix.Primary(part.EffectiveSlot(kind), crewDomain).Provider
                : part.Provider;

        if (spec.Executor is not null)
            actorBindings.Add(("executor", EffectiveProvider(spec.Executor, CrewPartKind.Executor), spec.Executor.ToolNames));

        for (var i = 0; i < spec.Reviewers.Count; i++)
            actorBindings.Add(($"reviewers[{i}]", EffectiveProvider(spec.Reviewers[i], CrewPartKind.Reviewer), spec.Reviewers[i].ToolNames));

        for (var i = 0; i < spec.Advisors.Count; i++)
            actorBindings.Add(($"advisors[{i}]", EffectiveProvider(spec.Advisors[i], CrewPartKind.Advisor), spec.Advisors[i].ToolNames));

        for (var i = 0; i < spec.Finalizers.Count; i++)
        {
            var provider = spec.Finalizers[i].IsInlineTransform() ? EffectiveProvider(spec.Finalizers[i], CrewPartKind.TransformFinalizer) : spec.Finalizers[i].Provider;
            actorBindings.Add(($"finalizers[{i}]", provider, spec.Finalizers[i].ToolNames));
        }

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

                // Check 8c – Mutating tools require explicit opt-in
                if (tool.AccessClass == ToolAccessClass.Mutating && !spec.AllowMutatingTools)
                {
                    issues.Add(new CrewSpecValidationIssue(
                        Field:      $"{path}.tool_names",
                        Message:    $"Tool '{toolName}' has AccessClass=Mutating. Mutating tools require explicit opt-in. " +
                                    "Set 'allow_mutating_tools: true' in the crew spec to enable Mutating tool access.",
                        IsCritical: true));
                }
            }
        }
    }

    /// <summary>
    /// Validates a single profile reference: resolves <c>reuse</c> names against the catalog, or checks the required
    /// inline fields. Models are never required inline: they come from the model matrix (D-066).
    /// </summary>
    private static async Task ValidateProfileRefAsync<TProfile>(
        CrewPartSpec profileRef,
        string fieldPath,
        Func<string, Task<TProfile?>> catalogLookup,
        List<CrewSpecValidationIssue> issues,
        CancellationToken cancellationToken,
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

    }

    private static void ValidateInlineGroundingSettings(CrewPartSpec part, string path, List<CrewSpecValidationIssue> issues)
    {
        if (part.ProviderType is { } type && CrewPartSettingsPolicy.ReuseOnlyGroundingTypes.Contains(type))
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{path}.provider_type",
                Message:    $"Grounding provider type '{type}' needs operator-configured targets and credentials; reuse an existing profile instead of defining it inline.",
                IsCritical: true));
        }

        if (part.ProviderType is { } droppedType && CrewPartSettingsPolicy.DroppedInlineGroundingTypes.Contains(droppedType))
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{path}.provider_type",
                Message:    $"Inline '{droppedType}' grounding providers are dropped: their targets may only come from a reused, operator-made profile.",
                IsCritical: false));
            return;
        }

        var dropped = part.DroppedGroundingKeys();
        if (dropped.Count > 0)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{path}.settings",
                Message:    $"Settings not allowed for inline grounding providers are ignored: {string.Join(", ", dropped)}.",
                IsCritical: false));
        }
    }

    private static void ValidateInlineFinalizerSettings(CrewPartSpec part, string path, List<CrewSpecValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(part.Reuse))
            return;

        if (!string.IsNullOrWhiteSpace(part.FinalizerType) && !CrewPartSettingsPolicy.TryParseFinalizerType(part.FinalizerType, out _))
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{path}.finalizer_type",
                Message:    $"Unknown finalizer_type '{part.FinalizerType}'. Valid values: {string.Join(", ", FinalizerTypeExtensions.Usable)}.",
                IsCritical: true));
            return;
        }

        var type = CrewPartSettingsPolicy.ParseFinalizerType(part.FinalizerType);

        var dropped = part.DroppedFinalizerKeys(type);
        if (dropped.Count > 0)
        {
            issues.Add(new CrewSpecValidationIssue(
                Field:      $"{path}.settings",
                Message:    $"Settings not allowed for inline {type} finalizers are ignored: {string.Join(", ", dropped)}.",
                IsCritical: false));
        }
    }
}
