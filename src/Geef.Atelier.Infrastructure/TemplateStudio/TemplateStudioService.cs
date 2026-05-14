using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

internal sealed class TemplateStudioService(
    ILlmClientResolver resolver,
    ICrewService crewService,
    IProviderCatalog providerCatalog,
    IPricingCatalog pricingCatalog,
    ITemplateStudioAnalysisRepository analysisRepository,
    ProfileSimilarityService similarityService,
    IOptions<TemplateStudioOptions> options) : ITemplateStudioService
{
    public async Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
    {
        var opts = options.Value;
        var context = await BuildContextAsync(ct);
        var systemPrompt = string.Format(TemplateStudioPrompts.MetaSystemPromptTemplate, context);

        var (client, model, maxTokens) = resolver.ForProfile("openrouter", opts.Model, opts.MaxTokens);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = systemPrompt,
            UserPrompt   = $"Task description: {taskDescription}\n\nAnalyse this task and call submit_template_proposal.",
            MaxTokens    = maxTokens,
            Tools        = [TemplateProposalTool.Schema],
            ToolChoice   = $"function:{TemplateProposalTool.ToolName}"
        }, ct);

        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
            throw new InvalidOperationException(
                $"Template Studio meta-LLM did not call the required tool (finish_reason='{response.FinishReason}').");

        var proposal = ParseProposal(response.ToolArgumentsJson);
        var deduplicated = await DeduplicateProfilesAsync(proposal.ProposedNewProfiles, opts.SimilarityThreshold, ct);

        var cost = pricingCatalog.CalculateCostEur(model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens);

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

    public async Task<MaterializationResult> MaterializeAsync(
        Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
    {
        ValidateNotSystemProfiles(request);

        var warnings = new List<string>();
        var createdProfileNames = new List<string>();

        foreach (var profile in request.FinalNewProfiles)
        {
            var name = await CreateProfileAsync(profile, warnings, ct);
            createdProfileNames.Add(name);
        }

        var templateName = await CreateTemplateAsync(request.FinalTemplate, ct);
        await analysisRepository.MarkMaterializedAsync(analysisId, templateName, ct);

        return new MaterializationResult(templateName, createdProfileNames, warnings);
    }

    // --- Private helpers ---

    private async Task<string> BuildContextAsync(CancellationToken ct)
    {
        var templates = await crewService.ListCrewTemplatesAsync(includeSystem: true, ct);
        var reviewers = await crewService.ListReviewerProfilesAsync(includeSystem: true, ct);
        var advisors  = await crewService.ListAdvisorProfilesAsync(includeSystem: true, ct);
        var grounding = await crewService.ListGroundingProviderProfilesAsync(includeSystem: true, ct);
        var executors = await crewService.ListExecutorProfilesAsync(includeSystem: true, ct);
        var providers = providerCatalog.ListProviders();

        return $"""
            Templates: {string.Join(", ", templates.Select(t => $"{t.Name} ({t.Description})"))}
            Executor profiles: {string.Join(", ", executors.Select(e => e.Name))}
            Reviewer profiles: {string.Join(", ", reviewers.Select(r => $"{r.Name} ({r.Description})"))}
            Advisor profiles: {string.Join(", ", advisors.Select(a => $"{a.Name} ({a.Description})"))}
            Grounding provider profiles: {string.Join(", ", grounding.Select(g => g.Name))}
            Available providers: {string.Join(", ", providers.Select(p => p.Name))}
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
                EvaluationStrategy:            ptEl.TryGetProperty("evaluation_strategy", out var evEl) ? evEl.GetString() ?? "Sequential" : "Sequential");
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
            _                    => ProposedProfileType.Reviewer
        };

        Dictionary<string, string>? groundingSettings = null;
        if (el.TryGetProperty("grounding_provider_settings", out var settingsEl) &&
            settingsEl.ValueKind == JsonValueKind.Object)
        {
            groundingSettings = settingsEl.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        return new ProposedProfile(
            ProfileType:               profileType,
            Name:                      el.GetProperty("name").GetString()!,
            DisplayName:               el.GetProperty("display_name").GetString()!,
            Description:               el.GetProperty("description").GetString()!,
            Model:                     el.GetProperty("model").GetString()!,
            Provider:                  el.GetProperty("provider").GetString()!,
            SystemPrompt:              el.GetProperty("system_prompt").GetString()!,
            MaxTokens:                 el.TryGetProperty("max_tokens", out var mtEl) && mtEl.ValueKind == JsonValueKind.Number ? mtEl.GetInt32() : null,
            ReviewerFocus:             el.TryGetProperty("reviewer_focus", out var rfEl)  ? rfEl.GetString()  : null,
            AdvisorMode:               el.TryGetProperty("advisor_mode", out var amEl)    ? amEl.GetString()  : null,
            AdvisorTrigger:            el.TryGetProperty("advisor_trigger", out var atEl) ? atEl.GetString()  : null,
            GroundingProviderType:     el.TryGetProperty("grounding_provider_type", out var gptEl) ? gptEl.GetString() : null,
            GroundingProviderSettings: groundingSettings);
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
            default:
                throw new NotSupportedException($"Profile type {profile.ProfileType} is not supported for creation.");
        }
    }

    private async Task<string> CreateTemplateAsync(ProposedTemplate template, CancellationToken ct)
    {
        var strategy = Enum.TryParse<EvaluationStrategy>(template.EvaluationStrategy, out var s)
            ? s : EvaluationStrategy.Sequential;

        var created = await crewService.CreateCustomCrewTemplateAsync(new CrewTemplate(
            Name:                  template.Name,
            DisplayName:           template.DisplayName,
            Description:           template.Description,
            ExecutorProfileName:   template.ExecutorProfileName,
            ReviewerProfileNames:  template.ReviewerProfileNames,
            EvaluationStrategy:    strategy,
            ConvergenceOverride:   null,
            AdvisorProfileNames:   template.AdvisorProfileNames,
            GroundingProviderNames: template.GroundingProviderProfileNames,
            IsSystem:              false), ct);

        return created.Name;
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
}
