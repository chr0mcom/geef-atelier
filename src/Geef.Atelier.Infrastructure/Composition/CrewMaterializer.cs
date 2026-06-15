using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Infrastructure.TemplateStudio;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Materializes a Crew-Spec JSON artifact into real database entities:
/// profiles (executor, reviewers, advisors, grounding providers, finalizers) and a crew template.
/// Performs crew-level deduplication via embedding similarity before creating new entities.
/// </summary>
internal sealed class CrewMaterializer(
    ICrewSpecValidator validator,
    ICrewService crewService,
    ICrewTemplateEmbeddingRepository embeddingRepo,
    IEmbeddingProvider embeddingProvider,
    IAtomicTransactionFactory transactionFactory,
    ILogger<CrewMaterializer> logger) : ICrewMaterializer
{
    /// <summary>Cosine-similarity threshold above which an existing crew is considered equivalent and reused.</summary>
    private const double DedupThreshold = 0.90;

    /// <summary>Domain-boost multiplier applied to same-domain results during dedup search.</summary>
    private const double SameDomainBoost = 1.5;

    /// <inheritdoc/>
    public async Task<MaterializeCrewResult> MaterializeAsync(
        string specJson,
        Guid sourceRunId,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Parse ────────────────────────────────────────────────────────────
        var spec = CrewSpecParser.Parse(specJson);
        logger.LogInformation(
            "CrewMaterializer: parsed spec for run {RunId} — mode={Mode}, domain={Domain}",
            sourceRunId, spec.Mode, spec.Domain);

        // ── 2. Validate ─────────────────────────────────────────────────────────
        var issues = await validator.ValidateAsync(specJson, cancellationToken);
        var criticals = issues.Where(i => i.IsCritical).ToList();
        if (criticals.Count > 0)
        {
            var messages = string.Join("; ", criticals.Select(c => $"[{c.Field}] {c.Message}"));
            logger.LogWarning(
                "CrewMaterializer: spec for run {RunId} has {Count} critical issue(s): {Messages}",
                sourceRunId, criticals.Count, messages);
            throw new InvalidOperationException(
                $"Crew-Spec validation failed with {criticals.Count} critical issue(s): {messages}");
        }

        var warnings = issues
            .Where(i => !i.IsCritical)
            .Select(i => $"[{i.Field}] {i.Message}")
            .ToList();

        // ── 3. ExistingTemplate mode ─────────────────────────────────────────────
        if (spec.Mode == Core.Domain.Crew.Composition.CrewSpecMode.ExistingTemplate)
        {
            var existingName = spec.ExistingTemplateName ?? string.Empty;
            var existing = await crewService.GetCrewTemplateAsync(existingName, cancellationToken);
            if (existing is null)
            {
                throw new InvalidOperationException(
                    $"Spec references existing template '{existingName}' but it was not found in the catalog.");
            }

            logger.LogInformation(
                "CrewMaterializer: run {RunId} — reusing existing template '{Template}'",
                sourceRunId, existing.Name);
            return new MaterializeCrewResult(existing.Name, WasDuplicate: true, warnings);
        }

        // ── 4. Crew-level dedup (Composed / New) ─────────────────────────────────
        var summaryText = $"{spec.Domain}: {spec.Rationale}";
        try
        {
            var embedding = await embeddingProvider.CreateAsync(summaryText, cancellationToken);
            var similar = await embeddingRepo.SearchAsync(
                embedding.Vector,
                domainHint: spec.Domain,
                SameDomainBoost,
                topK: 3,
                ct: cancellationToken);

            // Walk candidates (highest similarity first). Only reuse a candidate whose template
            // still EXISTS — embeddings can outlive their template (deleted template ⇒ orphan
            // embedding), and reusing a dead name makes the chained task run fail with "not found".
            foreach (var (entry, similarity) in similar)
            {
                if (similarity < DedupThreshold) break; // ordered by similarity desc

                var dupName = entry.TemplateName;
                var dupTemplate = await crewService.GetCrewTemplateAsync(dupName, cancellationToken);
                if (dupTemplate is null)
                {
                    // Orphaned embedding — clean it up and try the next candidate.
                    logger.LogWarning(
                        "CrewMaterializer: run {RunId} — dedup candidate '{Template}' has no template (orphaned embedding); deleting and skipping.",
                        sourceRunId, dupName);
                    try { await embeddingRepo.DeleteAsync(dupName, cancellationToken); }
                    catch (Exception delEx) { logger.LogWarning(delEx, "CrewMaterializer: failed to delete orphaned embedding '{Template}'.", dupName); }
                    continue;
                }

                logger.LogInformation(
                    "CrewMaterializer: run {RunId} — dedup hit; reusing existing template '{Template}' (similarity={Sim:F3})",
                    sourceRunId, dupName, similarity);
                warnings.Add($"Dedup: existing template '{dupName}' is similar ({similarity:F3}); reused instead of creating new.");
                return new MaterializeCrewResult(dupName, WasDuplicate: true, warnings);
            }
        }
        catch (Exception ex)
        {
            // Dedup failure must not block materialization — log and continue.
            logger.LogWarning(ex, "CrewMaterializer: run {RunId} — embedding dedup failed; proceeding to create new template.", sourceRunId);
            warnings.Add($"Dedup skipped due to embedding error: {ex.Message}");
        }

        // ── 5. Materialize inside a transaction ───────────────────────────────────
        string finalTemplateName;
        await using var transaction = await transactionFactory.BeginAsync(cancellationToken);
        try
        {
            // old-name → final-name mapping (CreateCustom* auto-prefixes with "custom-")
            var nameMapping = new Dictionary<string, string>(StringComparer.Ordinal);

            // 5a. Executor
            string executorName;
            if (spec.Executor is { Reuse: not null } reuseExec)
            {
                executorName = reuseExec.Reuse;
            }
            else if (spec.Executor is { Reuse: null } inlineExec)
            {
                var profile = BuildExecutorProfile(inlineExec);
                var created = await crewService.CreateCustomExecutorProfileAsync(profile, cancellationToken);
                nameMapping[inlineExec.Name!] = created.Name;
                executorName = created.Name;
                logger.LogDebug("CrewMaterializer: created executor '{Name}'", created.Name);
            }
            else
            {
                // Fall back to system default
                executorName = "default-executor";
                warnings.Add("No executor specified in spec; falling back to 'default-executor'.");
            }

            // 5b. Reviewers
            var reviewerNames = new List<string>();
            foreach (var part in spec.Reviewers)
            {
                if (part.Reuse is not null)
                {
                    reviewerNames.Add(part.Reuse);
                }
                else
                {
                    var profile = BuildReviewerProfile(part);
                    var created = await crewService.CreateCustomReviewerProfileAsync(profile, cancellationToken);
                    nameMapping[part.Name!] = created.Name;
                    reviewerNames.Add(created.Name);
                    logger.LogDebug("CrewMaterializer: created reviewer '{Name}'", created.Name);
                }
            }

            // 5c. Advisors
            var advisorNames = new List<string>();
            foreach (var part in spec.Advisors)
            {
                if (part.Reuse is not null)
                {
                    advisorNames.Add(part.Reuse);
                }
                else
                {
                    var profile = BuildAdvisorProfile(part);
                    var created = await crewService.CreateCustomAdvisorProfileAsync(profile, cancellationToken);
                    nameMapping[part.Name!] = created.Name;
                    advisorNames.Add(created.Name);
                    logger.LogDebug("CrewMaterializer: created advisor '{Name}'", created.Name);
                }
            }

            // 5d. Grounding providers
            var groundingNames = new List<string>();
            foreach (var part in spec.GroundingProviders)
            {
                if (part.Reuse is not null)
                {
                    groundingNames.Add(part.Reuse);
                }
                else
                {
                    var profile = BuildGroundingProviderProfile(part);
                    var created = await crewService.CreateCustomGroundingProviderProfileAsync(profile, cancellationToken);
                    nameMapping[part.Name!] = created.Name;
                    groundingNames.Add(created.Name);
                    logger.LogDebug("CrewMaterializer: created grounding provider '{Name}'", created.Name);
                }
            }

            // 5e. Finalizers
            var finalizerNames = new List<string>();
            foreach (var part in spec.Finalizers)
            {
                if (part.Reuse is not null)
                {
                    finalizerNames.Add(part.Reuse);
                }
                else
                {
                    var profile = BuildFinalizerProfile(part);
                    var created = await crewService.CreateCustomFinalizerProfileAsync(profile, cancellationToken);
                    nameMapping[part.Name!] = created.Name;
                    finalizerNames.Add(created.Name);
                    logger.LogDebug("CrewMaterializer: created finalizer '{Name}'", created.Name);
                }
            }

            // 5f. Resolve executor name through mapping in case it was just created
            executorName = nameMapping.GetValueOrDefault(executorName, executorName);

            // 5g. Build convergence override
            ConvergencePolicyOverride? convergenceOverride = null;
            if (spec.MaxIterations.HasValue || spec.AbortOnCritical.HasValue)
            {
                convergenceOverride = new ConvergencePolicyOverride(
                    MaxIterations:      spec.MaxIterations,
                    AbortOnCritical:    spec.AbortOnCritical,
                    DetectRegression:   null,
                    StagnationThreshold: null);
            }

            // 5h. Parse evaluation strategy
            var evaluationStrategy = ParseEvaluationStrategy(spec.EvaluationStrategy);

            // 5i. Build and create crew template
            var templateHint = CrewTemplateNaming.BuildAutoTemplateName(spec.Domain);
            var template = new CrewTemplate(
                Name:                   templateHint,
                DisplayName:            Truncate($"Auto-composed: {spec.Domain}", 199),
                Description:            Truncate(spec.Rationale, 200),
                ExecutorProfileName:    executorName,
                ReviewerProfileNames:   reviewerNames,
                EvaluationStrategy:     evaluationStrategy,
                ConvergenceOverride:    convergenceOverride,
                AdvisorProfileNames:    advisorNames,
                GroundingProviderNames: groundingNames,
                IsSystem:               false,
                FinalizerProfileNames:  finalizerNames);

            var createdTemplate = await crewService.CreateCustomCrewTemplateAsync(template, cancellationToken);
            finalTemplateName = createdTemplate.Name;
            logger.LogInformation(
                "CrewMaterializer: run {RunId} — created crew template '{Template}'",
                sourceRunId, finalTemplateName);

            // 5j. Specialization packs: create new ones (TaskBound → owned by this crew) and bind packs
            // to actors. Done after template creation so TaskBound packs can reference the final name.
            var packNameMapping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var packSpec in spec.NewPacks)
            {
                if (string.IsNullOrWhiteSpace(packSpec.Name) || string.IsNullOrWhiteSpace(packSpec.SpecializationText))
                    continue;

                var scope = ParsePackScope(packSpec.Scope);
                var created = await crewService.CreateCustomSpecializationPackAsync(new SpecializationPack(
                    Name:               packSpec.Name!,
                    DisplayName:        packSpec.DisplayName ?? packSpec.Name!,
                    Description:        "Auto-composed pack",
                    SpecializationText: packSpec.SpecializationText!,
                    Scope:              scope,
                    Domain:             scope == PackScope.DomainScoped ? (packSpec.Domain ?? spec.Domain) : null,
                    ApplicableActorTypes: ParseActorTypes(packSpec.ApplicableActorTypes),
                    OwningCrewId:       scope == PackScope.TaskBound ? finalTemplateName : null,
                    IsSystem:           false), cancellationToken);
                packNameMapping[packSpec.Name!] = created.Name;
                logger.LogDebug("CrewMaterializer: created pack '{Name}' (scope={Scope})", created.Name, scope);
            }

            var bindings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            AddPackBinding(bindings, ActorTypeKeys.Executor, executorName, spec.Executor?.PackNames, packNameMapping);
            for (var i = 0; i < reviewerNames.Count && i < spec.Reviewers.Count; i++)
                AddPackBinding(bindings, ActorTypeKeys.Reviewer, reviewerNames[i], spec.Reviewers[i].PackNames, packNameMapping);
            for (var i = 0; i < advisorNames.Count && i < spec.Advisors.Count; i++)
                AddPackBinding(bindings, ActorTypeKeys.Advisor, advisorNames[i], spec.Advisors[i].PackNames, packNameMapping);

            if (bindings.Count > 0)
                await crewService.UpdateCustomCrewTemplateAsync(
                    createdTemplate with { ActorPackBindings = bindings }, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // ── 6. Write embedding AFTER commit (failure must not roll back the crew) ──
        try
        {
            var embeddingResult = await embeddingProvider.CreateAsync(summaryText, cancellationToken);
            await embeddingRepo.UpsertAsync(
                new CrewTemplateEmbedding
                {
                    Id           = Guid.NewGuid(),
                    TemplateName = finalTemplateName,
                    Domain       = spec.Domain,
                    Summary      = summaryText,
                    Embedding    = embeddingResult.Vector,
                    CreatedAt    = DateTimeOffset.UtcNow,
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CrewMaterializer: run {RunId} — embedding write failed for template '{Template}'; crew was created successfully.",
                sourceRunId, finalTemplateName);
            warnings.Add($"Embedding write failed (non-fatal): {ex.Message}");
        }

        return new MaterializeCrewResult(finalTemplateName, WasDuplicate: false, warnings);
    }

    // ── Private builders ─────────────────────────────────────────────────────────

    // Floors guard against an LLM that lowballs max_tokens (e.g. 4096), which truncates the output.
    // A value above the floor is kept as-is; null or anything below is raised to the floor.
    private const int ExecutorMaxTokensFloor = 32000;
    private const int ReviewerMaxTokensFloor = 8000;
    private const int AdvisorMaxTokensFloor  = 8000;

    private static int ClampMaxTokens(int? value, int floor) =>
        value is { } v && v > floor ? v : floor;

    private static ExecutorProfile BuildExecutorProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:        part.Name!,
            DisplayName: Truncate(part.DisplayName ?? part.Name!, 199),
            Description: "Auto-composed executor",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-opus-4-8",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, ExecutorMaxTokensFloor),
            IsSystem:    false,
            ToolNames:   part.ToolNames);

    private static ReviewerProfile BuildReviewerProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:        part.Name!,
            DisplayName: Truncate(part.DisplayName ?? part.Name!, 199),
            Description: "Auto-composed reviewer",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-sonnet-4-6",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, ReviewerMaxTokensFloor),
            IsSystem:    false,
            ToolNames:   part.ToolNames);

    private static AdvisorProfile BuildAdvisorProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:        part.Name!,
            DisplayName: Truncate(part.DisplayName ?? part.Name!, 199),
            Description: "Auto-composed advisor",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-sonnet-4-6",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, AdvisorMaxTokensFloor),
            Mode:        ParseAdvisorMode(part.AdvisorMode),
            Trigger:     ParseAdvisorTrigger(part.AdvisorTrigger),
            IsSystem:    false,
            ToolNames:   part.ToolNames);

    private static GroundingProviderProfile BuildGroundingProviderProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:             part.Name!,
            DisplayName:      part.DisplayName ?? part.Name!,
            Description:      "Auto-composed grounding provider",
            ProviderType:     part.ProviderType ?? "tavily",
            ProviderSettings: [],
            MaxQueriesPerRun: null,
            IsSystem:         false);

    private static FinalizerProfile BuildFinalizerProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:          part.Name!,
            DisplayName:   part.DisplayName ?? part.Name!,
            Description:   "Auto-composed finalizer",
            FinalizerType: ParseFinalizerType(part.FinalizerType),
            Settings:      [],
            IsSystem:      false,
            ToolNames:     part.ToolNames);

    private static EvaluationStrategy ParseEvaluationStrategy(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "sequential" => EvaluationStrategy.Sequential,
            "failfast"   => EvaluationStrategy.FailFast,
            "priority"   => EvaluationStrategy.Priority,
            _            => EvaluationStrategy.Parallel,
        };

    private static AdvisorMode ParseAdvisorMode(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "critical"      => AdvisorMode.Critical,
            "devilsadvocate" => AdvisorMode.DevilsAdvocate,
            "domainexpert"  => AdvisorMode.DomainExpert,
            _               => AdvisorMode.Strategic,
        };

    private static AdvisorTrigger ParseAdvisorTrigger(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "beforeeveryexecution"  => AdvisorTrigger.BeforeEveryExecution,
            "onconvergencefailure"  => AdvisorTrigger.OnConvergenceFailure,
            _                      => AdvisorTrigger.BeforeFirstExecution,
        };

    private static void AddPackBinding(
        Dictionary<string, IReadOnlyList<string>> bindings,
        string actorType,
        string actorName,
        IReadOnlyList<string>? packNames,
        IReadOnlyDictionary<string, string> packNameMapping)
    {
        if (packNames is not { Count: > 0 }) return;
        var resolved = packNames
            .Select(n => packNameMapping.GetValueOrDefault(n, n))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        if (resolved.Count > 0)
            bindings[ActorTypeKeys.BindingKey(actorType, actorName)] = resolved;
    }

    private static PackScope ParsePackScope(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "general"      => PackScope.General,
            "domainscoped" => PackScope.DomainScoped,
            _              => PackScope.TaskBound,   // composer default
        };

    private static IReadOnlyList<PackActorType> ParseActorTypes(IReadOnlyList<string>? raw)
    {
        if (raw is not { Count: > 0 }) return [PackActorType.Any];
        var types = raw
            .Select(s => s?.ToLowerInvariant() switch
            {
                "executor"  => PackActorType.Executor,
                "reviewer"  => PackActorType.Reviewer,
                "advisor"   => PackActorType.Advisor,
                "grounding" => PackActorType.Grounding,
                "finalizer" => PackActorType.Finalizer,
                _           => PackActorType.Any,
            })
            .Distinct()
            .ToList();
        return types.Count > 0 ? types : [PackActorType.Any];
    }

    private static FinalizerType ParseFinalizerType(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "metadataenrich" => FinalizerType.MetadataEnrich,
            "externalsink"   => FinalizerType.ExternalSink,
            "transform"      => FinalizerType.Transform,
            "learningextract" => FinalizerType.LearningExtract,
            "learningpublish" => FinalizerType.LearningPublish,
            "crewmaterialize" => FinalizerType.CrewMaterialize,
            _                => FinalizerType.FileExport,
        };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
