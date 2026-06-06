using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
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

            if (similar.Count > 0 && similar[0].Similarity >= DedupThreshold)
            {
                var dupName = similar[0].Entry.TemplateName;
                logger.LogInformation(
                    "CrewMaterializer: run {RunId} — dedup hit; reusing existing template '{Template}' (similarity={Sim:F3})",
                    sourceRunId, dupName, similar[0].Similarity);
                warnings.Add($"Dedup: existing template '{dupName}' is similar ({similar[0].Similarity:F3}); reused instead of creating new.");
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
                DisplayName:            $"Auto-composed: {spec.Domain}",
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
            DisplayName: part.DisplayName ?? part.Name!,
            Description: "Auto-composed executor",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-opus-4-8",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, ExecutorMaxTokensFloor),
            IsSystem:    false);

    private static ReviewerProfile BuildReviewerProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:        part.Name!,
            DisplayName: part.DisplayName ?? part.Name!,
            Description: "Auto-composed reviewer",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-sonnet-4-6",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, ReviewerMaxTokensFloor),
            IsSystem:    false);

    private static AdvisorProfile BuildAdvisorProfile(Core.Domain.Crew.Composition.CrewPartSpec part) =>
        new(
            Name:        part.Name!,
            DisplayName: part.DisplayName ?? part.Name!,
            Description: "Auto-composed advisor",
            SystemPrompt: part.SystemPrompt ?? string.Empty,
            Provider:    part.Provider ?? "claude-cli",
            Model:       part.Model ?? "claude-sonnet-4-6",
            MaxTokens:   ClampMaxTokens(part.MaxTokens, AdvisorMaxTokensFloor),
            Mode:        ParseAdvisorMode(part.AdvisorMode),
            Trigger:     ParseAdvisorTrigger(part.AdvisorTrigger),
            IsSystem:    false);

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
            IsSystem:      false);

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
