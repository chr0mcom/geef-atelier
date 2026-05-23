using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

internal sealed class TemplateStudioService(
    ILlmClientResolver resolver,
    ICrewService crewService,
    IProviderCatalog providerCatalog,
    IModelCatalog modelCatalog,
    IPricingCatalog pricingCatalog,
    ITemplateStudioAnalysisRepository analysisRepository,
    ProfileSimilarityService similarityService,
    IAtomicTransactionFactory transactionFactory,
    IStudioSettingsService studioSettings,
    ILogger<TemplateStudioService> logger,
    IOptions<TemplateStudioOptions> options) : ITemplateStudioService
{
    public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
        => AnalyzeAsync(taskDescription, overrideChoice: null, progress: null, ct);

    public Task<TemplateStudioAnalysis> AnalyzeAsync(
        string taskDescription, StudioModelChoice? overrideChoice, CancellationToken ct = default)
        => AnalyzeAsync(taskDescription, overrideChoice, progress: null, ct);

    public async Task<TemplateStudioAnalysis> AnalyzeAsync(
        string taskDescription, StudioModelChoice? overrideChoice, IProgress<string>? progress, CancellationToken ct = default)
    {
        var opts = options.Value;

        var choice = await ResolveChoiceAsync(overrideChoice, ct);
        logger.LogInformation("Studio.AnalyzeAsync start: provider={Provider} model={Model} maxTokens={MaxTokens}",
            choice.Provider, choice.Model, choice.MaxTokens);

        progress?.Report("Sammle verfügbare Modelle der Provider…");
        var context = await BuildContextAsync(ct);
        var systemPrompt = TemplateStudioPrompts.MetaSystemPromptTemplate.Replace("{0}", context);

        var (client, model, maxTokens) = resolver.ForProfile(choice.Provider, choice.Model, choice.MaxTokens);

        logger.LogInformation("Studio.AnalyzeAsync: context built, calling meta-LLM {Provider}/{Model}", choice.Provider, model);
        progress?.Report($"Frage Meta-KI an: {choice.Provider} / {model}…");

        // Hard cap on the meta-LLM call so a stalled provider can never hang the analysis indefinitely.
        using var llmCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        llmCts.CancelAfter(TimeSpan.FromSeconds(150));

        LlmResponse response;
        try
        {
            response = await client.CompleteAsync(new LlmRequest
            {
                Model        = model,
                SystemPrompt = systemPrompt,
                UserPrompt   = $"Task description: {taskDescription}\n\nAnalyse this task and call submit_template_proposal.",
                MaxTokens    = maxTokens,
                Tools        = [TemplateProposalTool.Schema],
                ToolChoice   = $"function:{TemplateProposalTool.ToolName}"
            }, llmCts.Token);
        }
        catch (OperationCanceledException) when (llmCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Die Meta-KI ({choice.Provider} / {model}) hat nicht innerhalb von 150 Sekunden geantwortet. " +
                "Bitte erneut versuchen oder einen anderen Provider/Modell wählen.");
        }

        logger.LogInformation("Studio.AnalyzeAsync: meta-LLM responded finish={Finish} hasTool={HasTool}",
            response.FinishReason, response.ToolArgumentsJson is not null);
        progress?.Report("Verarbeite Vorschlag…");

        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
            throw new InvalidOperationException(
                $"Template Studio meta-LLM did not call the required tool (finish_reason='{response.FinishReason}').");

        var proposal = ParseProposal(response.ToolArgumentsJson);
        var withDefaults = proposal.ProposedNewProfiles.Select(p => ApplyDefaults(p, opts.Defaults)).ToList();
        var deduplicated = await DeduplicateProfilesAsync(withDefaults, opts.SimilarityThreshold, ct);

        var cost = pricingCatalog.CalculateCostEur(model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, choice.Provider);

        var analysis = new TemplateStudioAnalysis(
            Id:                     Guid.NewGuid(),
            TaskDescription:        taskDescription,
            MatchedExistingTemplates: proposal.MatchedExistingTemplates,
            Recommendation:         proposal.Recommendation,
            ProposedTemplate:       proposal.ProposedTemplate,
            ProposedNewProfiles:    deduplicated,
            ReasoningSummary:       proposal.ReasoningSummary,
            InputTokens:            response.TokenUsage.InputTokens,
            OutputTokens:           response.TokenUsage.OutputTokens,
            CostEur:                cost,
            CreatedAt:              DateTimeOffset.UtcNow);

        await analysisRepository.CreateAsync(analysis, ct);
        return analysis;
    }

    public async Task<StudioModelChoice> GetEffectiveDefaultAsync(CancellationToken ct = default)
        => await ResolveChoiceAsync(overrideChoice: null, ct);

    public async Task SaveDefaultAsync(StudioModelChoice choice, CancellationToken ct = default)
    {
        var opts = options.Value;
        await studioSettings.UpdateAsync(new Core.Domain.StudioSettings
        {
            Provider  = string.IsNullOrWhiteSpace(choice.Provider) ? opts.Provider : choice.Provider.Trim(),
            Model     = string.IsNullOrWhiteSpace(choice.Model)    ? opts.Model    : choice.Model.Trim(),
            MaxTokens = choice.MaxTokens > 0 ? choice.MaxTokens : opts.MaxTokens,
        }, ct);
    }

    // Resolution order: explicit per-analysis override → persisted default → appsettings default.
    private async Task<StudioModelChoice> ResolveChoiceAsync(StudioModelChoice? overrideChoice, CancellationToken ct)
    {
        var opts = options.Value;

        if (overrideChoice is not null &&
            !string.IsNullOrWhiteSpace(overrideChoice.Provider) &&
            !string.IsNullOrWhiteSpace(overrideChoice.Model))
        {
            return new StudioModelChoice(
                overrideChoice.Provider.Trim(),
                overrideChoice.Model.Trim(),
                overrideChoice.MaxTokens > 0 ? overrideChoice.MaxTokens : opts.MaxTokens);
        }

        var persisted = await studioSettings.GetAsync(ct);
        if (!string.IsNullOrWhiteSpace(persisted.Provider) && !string.IsNullOrWhiteSpace(persisted.Model))
        {
            return new StudioModelChoice(
                persisted.Provider,
                persisted.Model,
                persisted.MaxTokens > 0 ? persisted.MaxTokens : opts.MaxTokens);
        }

        return new StudioModelChoice(opts.Provider, opts.Model, opts.MaxTokens);
    }

    public async Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var (items, hasMore) = await analysisRepository.ListHistoryAsync(page, pageSize, ct);
        var entries = items
            .Select(i => new StudioAnalysisHistoryEntry(
                i.Id, i.TaskDescription, i.ReasoningSummary,
                i.MaterializedTemplateName, i.CostEur, i.CreatedAt))
            .ToList();
        return new StudioAnalysesPage(entries, hasMore);
    }

    public async Task<MaterializationResult> MaterializeAsync(
        Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
    {
        ValidateNotSystemProfiles(request);
        ValidateReviewerCount(request.FinalTemplate);

        var warnings = new List<string>();
        await ValidateAvailabilityAsync(request.FinalNewProfiles, warnings, ct);

        await using var transaction = await transactionFactory.BeginAsync(ct);
        try
        {
            // Track old→final name mapping so template references are updated to actual stored names.
            // CreateCustom*Async auto-prefixes with "custom-", so "my-profile" becomes "custom-my-profile".
            var nameMapping = new Dictionary<string, string>(StringComparer.Ordinal);
            var createdProfileNames = new List<string>();
            foreach (var profile in request.FinalNewProfiles)
            {
                var finalName = await CreateProfileAsync(profile, warnings, ct);
                createdProfileNames.Add(finalName);
                nameMapping[profile.Name] = finalName;
            }

            var resolvedTemplate = ApplyProfileNameMapping(request.FinalTemplate, nameMapping);
            var templateName = await CreateTemplateAsync(resolvedTemplate, ct);
            await analysisRepository.MarkMaterializedAsync(analysisId, templateName, ct);

            await transaction.CommitAsync(ct);
            return new MaterializationResult(templateName, createdProfileNames, warnings);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // --- Private helpers ---

    private async Task<string> BuildContextAsync(CancellationToken ct)
    {
        logger.LogInformation("Studio.BuildContext: loading crew lists…");
        var templates  = await crewService.ListCrewTemplatesAsync(includeSystem: true, ct);
        var reviewers  = await crewService.ListReviewerProfilesAsync(includeSystem: true, ct);
        var advisors   = await crewService.ListAdvisorProfilesAsync(includeSystem: true, ct);
        var grounding  = await crewService.ListGroundingProviderProfilesAsync(includeSystem: true, ct);
        var executors  = await crewService.ListExecutorProfilesAsync(includeSystem: true, ct);
        var finalizers = await crewService.ListFinalizerProfilesAsync(includeSystem: true, ct);
        var providers  = providerCatalog.ListProviders();
        logger.LogInformation("Studio.BuildContext: crew lists loaded, fetching models for {Count} providers in parallel…", providers.Count);

        // Fetch every provider's model list in PARALLEL with an overall budget. A single slow or
        // unreachable provider can no longer serialize/stall the whole analysis: the budget caps
        // total wait time and any provider that exceeds it (or errors) contributes no models.
        using var modelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        modelCts.CancelAfter(TimeSpan.FromSeconds(20));

        var fetches = providers.Select(async provider =>
        {
            try
            {
                var models = await modelCatalog.ListModelsAsync(provider.Name, modelCts.Token);
                return (provider.Name, Models: models);
            }
            catch
            {
                return (provider.Name, Models: (IReadOnlyList<ModelInfo>)[]);
            }
        });
        var fetched = await Task.WhenAll(fetches);
        var modelsByProvider = fetched.ToDictionary(f => f.Name, f => f.Models);
        logger.LogInformation("Studio.BuildContext: model fetch complete ({Total} models across {Providers} providers)",
            fetched.Sum(f => f.Models.Count), fetched.Length);

        // Build per-provider model lists (preserving provider order) so the LLM can pick accurate IDs.
        var modelLines = new List<string>();
        foreach (var provider in providers)
        {
            var models = modelsByProvider.TryGetValue(provider.Name, out var m) ? m : [];
            var recommended = models.Where(m => m.IsRecommended).Select(m => m.Id).ToList();
            var rest = models.Where(m => !m.IsRecommended).Select(m => m.Id).Take(15).ToList();
            if (recommended.Count > 0)
                modelLines.Add($"  {provider.Name} RECOMMENDED: {string.Join(", ", recommended)}");
            if (rest.Count > 0)
                modelLines.Add($"  {provider.Name} also available: {string.Join(", ", rest)}");
        }
        var modelsBlock = modelLines.Count > 0
            ? string.Join("\n", modelLines)
            : "  (no model catalog available — use provider defaults)";

        return $"""
            Templates: {string.Join(", ", templates.Select(t => $"{t.Name} ({t.Description})"))}
            Executor profiles: {string.Join(", ", executors.Select(e => e.Name))}
            Reviewer profiles: {string.Join(", ", reviewers.Select(r => $"{r.Name} ({r.Description})"))}
            Advisor profiles: {string.Join(", ", advisors.Select(a => $"{a.Name} ({a.Description})"))}
            Grounding provider profiles: {string.Join(", ", grounding.Select(g => g.Name))}
            Finalizer profiles: {string.Join(", ", finalizers.Select(f => $"{f.Name} ({f.FinalizerType})"))}
            Available providers and their current models (use ONLY these exact IDs):
            {modelsBlock}
            """;
    }

    private (IReadOnlyList<TemplateMatch> MatchedExistingTemplates, StudioRecommendation Recommendation,
             ProposedTemplate? ProposedTemplate, IReadOnlyList<ProposedProfile> ProposedNewProfiles,
             string ReasoningSummary) ParseProposal(string toolArgumentsJson)
    {
        using var doc = JsonDocument.Parse(toolArgumentsJson);
        var root = doc.RootElement;

        var matches = root.TryGetProperty("matched_existing_templates", out var matchesEl)
            ? matchesEl.EnumerateArray().Select(m => new TemplateMatch(
                TemplateName: m.GetProperty("template_name").GetString()!,
                Confidence:   m.GetProperty("confidence").GetDouble(),
                Reasoning:    m.GetProperty("reasoning").GetString()!)).ToList()
            : (List<TemplateMatch>)[];

        var recommendationStr = root.TryGetProperty("recommendation", out var recEl)
            ? recEl.GetString() : null;
        var recommendation = recommendationStr switch
        {
            "use_existing"   => StudioRecommendation.UseExistingTemplate,
            "adapt_existing" => StudioRecommendation.AdaptExistingTemplate,
            _                => StudioRecommendation.CreateNewTemplate
        };

        ProposedTemplate? proposedTemplate = null;
        if (root.TryGetProperty("proposed_template", out var ptEl) && ptEl.ValueKind == JsonValueKind.Object)
        {
            proposedTemplate = new ProposedTemplate(
                Name:                          ptEl.GetProperty("name").GetString()!,
                DisplayName:                   ptEl.GetProperty("display_name").GetString()!,
                Description:                   ptEl.GetProperty("description").GetString()!,
                ExecutorProfileName:           ptEl.TryGetProperty("executor_profile_name", out var ep) ? ep.GetString()! : "default-executor",
                ReviewerProfileNames:          GetStringArray(ptEl, "reviewer_profile_names"),
                AdvisorProfileNames:           GetStringArray(ptEl, "advisor_profile_names"),
                GroundingProviderProfileNames: GetStringArray(ptEl, "grounding_provider_profile_names"),
                EvaluationStrategy:            NormalizeEvaluationStrategy(ptEl.TryGetProperty("evaluation_strategy", out var evEl) ? evEl.GetString() : null),
                EvaluationStrategyReasoning:   ptEl.TryGetProperty("evaluation_strategy_reasoning", out var esrEl) ? esrEl.GetString() : null,
                FinalizerProfileNames:         GetStringArray(ptEl, "finalizer_profile_names").Count > 0
                                                   ? GetStringArray(ptEl, "finalizer_profile_names") : null,
                RunFinalizersOnMaxAttempts:    ptEl.TryGetProperty("run_finalizers_on_max_attempts", out var rfEl) && rfEl.GetBoolean(),
                FinalizerReasoning:            ptEl.TryGetProperty("finalizer_reasoning", out var frEl) ? frEl.GetString() : null);
        }

        var newProfiles = root.TryGetProperty("proposed_new_profiles", out var profilesEl)
            ? profilesEl.EnumerateArray().Select(ParseProposedProfile).ToList()
            : (List<ProposedProfile>)[];

        var reasoningSummary = root.TryGetProperty("reasoning_summary", out var rsEl)
            ? rsEl.GetString()! : "";

        return (matches, recommendation, proposedTemplate, newProfiles, reasoningSummary);
    }

    private static ProposedProfile ParseProposedProfile(JsonElement el)
    {
        var typeStr = el.TryGetProperty("profile_type", out var typeEl) ? typeEl.GetString() : "reviewer";
        var profileType = typeStr switch
        {
            "advisor"            => ProposedProfileType.Advisor,
            "grounding_provider" => ProposedProfileType.GroundingProvider,
            "executor"           => ProposedProfileType.Executor,
            "finalizer"          => ProposedProfileType.Finalizer,
            _                    => ProposedProfileType.Reviewer
        };

        Dictionary<string, string>? groundingSettings = null;
        if (el.TryGetProperty("grounding_provider_settings", out var settingsEl) &&
            settingsEl.ValueKind == JsonValueKind.Object)
        {
            groundingSettings = settingsEl.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        Dictionary<string, string>? finalizerSettings = null;
        if (el.TryGetProperty("finalizer_settings", out var fsEl) && fsEl.ValueKind == JsonValueKind.Object)
        {
            finalizerSettings = fsEl.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        return new ProposedProfile(
            ProfileType:               profileType,
            Name:                      el.GetProperty("name").GetString()!,
            DisplayName:               el.GetProperty("display_name").GetString()!,
            Description:               el.GetProperty("description").GetString()!,
            Model:                     el.TryGetProperty("model", out var mEl)    ? mEl.GetString() ?? "" : "",
            Provider:                  el.TryGetProperty("provider", out var pEl) ? pEl.GetString() ?? "" : "",
            SystemPrompt:              el.TryGetProperty("system_prompt", out var spEl) ? spEl.GetString() ?? "" : "",
            MaxTokens:                 el.TryGetProperty("max_tokens", out var mtEl) && mtEl.ValueKind == JsonValueKind.Number ? mtEl.GetInt32() : null,
            ReviewerFocus:             el.TryGetProperty("reviewer_focus", out var rfEl)  ? rfEl.GetString()  : null,
            AdvisorMode:               el.TryGetProperty("advisor_mode", out var amEl)    ? amEl.GetString()  : null,
            AdvisorTrigger:            el.TryGetProperty("advisor_trigger", out var atEl) ? atEl.GetString()  : null,
            GroundingProviderType:     el.TryGetProperty("grounding_provider_type", out var gptEl) ? gptEl.GetString() : null,
            GroundingProviderSettings: groundingSettings,
            FinalizerType:             el.TryGetProperty("finalizer_type", out var ftEl)  ? ftEl.GetString()  : null,
            FinalizerSettings:         finalizerSettings,
            ModelReasoning:            el.TryGetProperty("model_reasoning", out var mrEl)           ? mrEl.GetString()  : null,
            SystemPromptReasoning:     el.TryGetProperty("system_prompt_reasoning", out var sprEl)  ? sprEl.GetString() : null,
            OverallReasoning:          el.TryGetProperty("overall_reasoning", out var orEl)         ? orEl.GetString()  : null,
            ModeReasoning:             el.TryGetProperty("mode_reasoning", out var modEl)           ? modEl.GetString() : null,
            TriggerReasoning:          el.TryGetProperty("trigger_reasoning", out var trEl)         ? trEl.GetString()  : null,
            FinalizerReasoning:        el.TryGetProperty("finalizer_reasoning", out var frEl)       ? frEl.GetString()  : null);
    }

    private static ProposedProfile ApplyDefaults(ProposedProfile p, StudioDefaults d) => p with
    {
        Model    = string.IsNullOrEmpty(p.Model)    ? GetDefaultModel(p.ProfileType, d)    : p.Model,
        Provider = string.IsNullOrEmpty(p.Provider) ? GetDefaultProvider(p.ProfileType, d) : p.Provider,
        MaxTokens = ClampMaxTokens(p.ProfileType, p.MaxTokens ?? GetDefaultMaxTokens(p.ProfileType, d)),
        AdvisorMode    = p.ProfileType == ProposedProfileType.Advisor && string.IsNullOrEmpty(p.AdvisorMode)    ? d.AdvisorMode    : p.AdvisorMode,
        AdvisorTrigger = p.ProfileType == ProposedProfileType.Advisor && string.IsNullOrEmpty(p.AdvisorTrigger) ? d.AdvisorTrigger : p.AdvisorTrigger,
        GroundingProviderType = p.ProfileType == ProposedProfileType.GroundingProvider && string.IsNullOrEmpty(p.GroundingProviderType) ? d.GroundingProviderType : p.GroundingProviderType
    };

    private static string GetDefaultModel(ProposedProfileType type, StudioDefaults d) => type switch
    {
        ProposedProfileType.Executor          => d.ExecutorModel,
        ProposedProfileType.Advisor           => d.AdvisorModel,
        ProposedProfileType.GroundingProvider => string.Empty,
        ProposedProfileType.Finalizer         => string.Empty, // only Transform finalizers use a model
        _                                     => d.ReviewerModel
    };

    private static string GetDefaultProvider(ProposedProfileType type, StudioDefaults d) => type switch
    {
        ProposedProfileType.Executor          => d.ExecutorProvider,
        ProposedProfileType.Advisor           => d.AdvisorProvider,
        ProposedProfileType.GroundingProvider => d.GroundingProviderProvider,
        ProposedProfileType.Finalizer         => string.Empty,
        _                                     => d.ReviewerProvider
    };

    private static int? GetDefaultMaxTokens(ProposedProfileType type, StudioDefaults d) => type switch
    {
        ProposedProfileType.Executor          => d.ExecutorMaxTokens,
        ProposedProfileType.Advisor           => d.AdvisorMaxTokens,
        ProposedProfileType.GroundingProvider => null,
        ProposedProfileType.Finalizer         => null,
        _                                     => d.ReviewerMaxTokens
    };

    // Grounding providers and non-Transform finalizers do no LLM generation; every generating
    // profile is clamped up to the hard floor so a small meta-LLM-proposed value cannot truncate.
    private static int? ClampMaxTokens(ProposedProfileType type, int? maxTokens) =>
        type is ProposedProfileType.GroundingProvider or ProposedProfileType.Finalizer
            ? null
            : Math.Max(maxTokens ?? StudioDefaults.MinMaxTokens, StudioDefaults.MinMaxTokens);

    private static void ValidateReviewerCount(ProposedTemplate template)
    {
        if (template.ReviewerProfileNames.Count == 0)
            throw new InvalidOperationException(
                "A crew template must have at least one reviewer profile. Add a reviewer before saving.");
    }

    private async Task<IReadOnlyList<ProposedProfile>> DeduplicateProfilesAsync(
        IReadOnlyList<ProposedProfile> profiles, double threshold, CancellationToken ct)
    {
        var result = new List<ProposedProfile>();
        foreach (var profile in profiles)
        {
            var (isDuplicate, _) = await similarityService.FindSimilarAsync(profile, threshold, ct);
            if (!isDuplicate) result.Add(profile);
        }
        return result;
    }

    private static void ValidateNotSystemProfiles(MaterializationRequest request)
    {
        if (request.FinalNewProfiles.Any(p => SystemCrew.IsSystemName(p.Name)))
            throw new InvalidOperationException(
                "Template Studio cannot create profiles with system-reserved names. Proposed profile names must not match system profile names.");
    }

    private async Task<string> CreateProfileAsync(ProposedProfile profile, List<string> warnings, CancellationToken ct)
    {
        switch (profile.ProfileType)
        {
            case ProposedProfileType.Reviewer:
            {
                var created = await crewService.CreateCustomReviewerProfileAsync(new ReviewerProfile(
                    Name:         profile.Name,
                    DisplayName:  profile.DisplayName,
                    Description:  profile.Description,
                    SystemPrompt: profile.SystemPrompt,
                    Provider:     profile.Provider,
                    Model:        profile.Model,
                    MaxTokens:    profile.MaxTokens,
                    IsSystem:     false), ct);
                return created.Name;
            }
            case ProposedProfileType.Advisor:
            {
                var mode    = Enum.TryParse<AdvisorMode>(profile.AdvisorMode    ?? "Strategic",            out var m) ? m : AdvisorMode.Strategic;
                var trigger = Enum.TryParse<AdvisorTrigger>(profile.AdvisorTrigger ?? "BeforeFirstExecution", out var t) ? t : AdvisorTrigger.BeforeFirstExecution;
                var created = await crewService.CreateCustomAdvisorProfileAsync(new AdvisorProfile(
                    Name:         profile.Name,
                    DisplayName:  profile.DisplayName,
                    Description:  profile.Description,
                    SystemPrompt: profile.SystemPrompt,
                    Provider:     profile.Provider,
                    Model:        profile.Model,
                    MaxTokens:    profile.MaxTokens,
                    Mode:         mode,
                    Trigger:      trigger,
                    IsSystem:     false), ct);
                return created.Name;
            }
            case ProposedProfileType.GroundingProvider:
            {
                var settings      = profile.GroundingProviderSettings ?? new Dictionary<string, string>();
                var providerType  = profile.GroundingProviderType ?? "tavily";
                var availableProviders = providerCatalog.ListProviders().Select(p => p.Name).ToHashSet();
                if (!availableProviders.Contains(profile.Provider))
                    warnings.Add($"Provider '{profile.Provider}' for grounding profile '{profile.Name}' requires configuration.");
                var created = await crewService.CreateCustomGroundingProviderProfileAsync(new GroundingProviderProfile(
                    Name:             profile.Name,
                    DisplayName:      profile.DisplayName,
                    Description:      profile.Description,
                    ProviderType:     providerType,
                    ProviderSettings: settings,
                    MaxQueriesPerRun: null,
                    IsSystem:         false), ct);
                return created.Name;
            }
            case ProposedProfileType.Executor:
            {
                var created = await crewService.CreateCustomExecutorProfileAsync(new ExecutorProfile(
                    Name:         profile.Name,
                    DisplayName:  profile.DisplayName,
                    Description:  profile.Description,
                    SystemPrompt: profile.SystemPrompt,
                    Provider:     profile.Provider,
                    Model:        profile.Model,
                    MaxTokens:    profile.MaxTokens,
                    IsSystem:     false), ct);
                return created.Name;
            }
            case ProposedProfileType.Finalizer:
            {
                var finalizerType = Enum.TryParse<FinalizerType>(profile.FinalizerType ?? "FileExport", out var ft)
                    ? ft : FinalizerType.FileExport;
                var settings = profile.FinalizerSettings ?? new Dictionary<string, string>();
                var created = await crewService.CreateCustomFinalizerProfileAsync(new FinalizerProfile(
                    Name:          profile.Name,
                    DisplayName:   profile.DisplayName,
                    Description:   profile.Description,
                    FinalizerType: finalizerType,
                    Settings:      settings,
                    IsSystem:      false), ct);
                return created.Name;
            }
            default:
                throw new NotSupportedException($"Profile type {profile.ProfileType} is not supported for creation.");
        }
    }

    // Maps profile names referenced by a template through a dictionary of old→final names.
    // Names not in the dictionary (existing profiles) are passed through unchanged.
    private static ProposedTemplate ApplyProfileNameMapping(ProposedTemplate template, Dictionary<string, string> map)
    {
        string Resolve(string name) => map.TryGetValue(name, out var final) ? final : name;
        return template with
        {
            ExecutorProfileName           = Resolve(template.ExecutorProfileName),
            ReviewerProfileNames          = template.ReviewerProfileNames.Select(Resolve).ToList(),
            AdvisorProfileNames           = template.AdvisorProfileNames.Select(Resolve).ToList(),
            GroundingProviderProfileNames = template.GroundingProviderProfileNames.Select(Resolve).ToList(),
            FinalizerProfileNames         = template.FinalizerProfileNames?.Select(Resolve).ToList(),
        };
    }

    private async Task<string> CreateTemplateAsync(ProposedTemplate template, CancellationToken ct)
    {
        var strategy = Enum.TryParse<EvaluationStrategy>(template.EvaluationStrategy, out var s)
            ? s : EvaluationStrategy.Sequential;

        var created = await crewService.CreateCustomCrewTemplateAsync(new CrewTemplate(
            Name:                      template.Name,
            DisplayName:               template.DisplayName,
            Description:               template.Description,
            ExecutorProfileName:       template.ExecutorProfileName,
            ReviewerProfileNames:      template.ReviewerProfileNames,
            EvaluationStrategy:        strategy,
            ConvergenceOverride:       null,
            AdvisorProfileNames:       template.AdvisorProfileNames,
            GroundingProviderNames:    template.GroundingProviderProfileNames,
            IsSystem:                  false,
            FinalizerProfileNames:     template.FinalizerProfileNames ?? [],
            RunFinalizersOnMaxAttempts: template.RunFinalizersOnMaxAttempts), ct);

        return created.Name;
    }

    private async Task ValidateAvailabilityAsync(
        IReadOnlyList<ProposedProfile> profiles, List<string> warnings, CancellationToken ct)
    {
        foreach (var profile in profiles)
        {
            if (profile.ProfileType is ProposedProfileType.GroundingProvider or ProposedProfileType.Finalizer)
                continue; // These profile types use dedicated non-LLM backends, not model catalog entries.

            try
            {
                var models = await modelCatalog.ListModelsAsync(profile.Provider, ct);
                var isAvailable = models.Any(m =>
                    string.Equals(m.Id, profile.Model, StringComparison.OrdinalIgnoreCase));
                if (!isAvailable)
                    warnings.Add(
                        $"Model '{profile.Model}' is not currently available from provider '{profile.Provider}'. Please verify before saving.");
            }
            catch
            {
                // Network or provider issue — skip availability check for this profile gracefully.
            }
        }
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var arrEl) || arrEl.ValueKind != JsonValueKind.Array)
            return [];
        return arrEl.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static string NormalizeEvaluationStrategy(string? raw) => raw?.Trim() switch
    {
        { } s when s.Equals("Parallel",   StringComparison.OrdinalIgnoreCase) => "Parallel",
        { } s when s.Equals("FailFast",   StringComparison.OrdinalIgnoreCase)
                || s.Equals("fail_fast",  StringComparison.OrdinalIgnoreCase)
                || s.Equals("FailFast",   StringComparison.Ordinal)           => "FailFast",
        { } s when s.Equals("Priority",   StringComparison.OrdinalIgnoreCase) => "Priority",
        { } s when s.Equals("Sequential", StringComparison.OrdinalIgnoreCase) => "Sequential",
        _ => "Sequential"
    };
}
