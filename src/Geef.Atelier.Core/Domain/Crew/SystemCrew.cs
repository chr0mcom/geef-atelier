using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Read-only catalogue of system-defined crew profiles and templates. These constants are
/// versioned with the Atelier source and ship as immutable defaults; user-editable variants
/// live in the database under the <c>"custom-"</c> name prefix.
/// </summary>
/// <remarks>
/// <para>Model defaults follow the Atelier model-pluralism convention (Vision doc, Leitstern 3 +
/// CLAUDE.md "Reviewer-Modell außerhalb der Anthropic-Familie"): the executor stays on Anthropic
/// (continuity with PS-2), reviewers use external models for genuine outside perspective.</para>
/// <para>If a chosen reviewer model is unavailable on OpenRouter or regresses against the
/// Hadwiger-Nelson replay (PS-5 AC 10), fall back to <c>anthropic/claude-opus-4.7</c> for all
/// reviewers and document the change in the PS-5 report.</para>
/// </remarks>
public static class SystemCrew
{
    /// <summary>Prefix automatically applied to user-created profile/template names to prevent collisions with system entries.</summary>
    public const string CustomPrefix = "custom-";

    /// <summary>Name of the only system template shipped with PS-5.</summary>
    public const string KlassikTemplateName = "klassik";

    /// <summary>Default executor profile — drafting LLM with Atelier's standard prompt.</summary>
    public static readonly ExecutorProfile DefaultExecutorProfile = new(
        Name: "default-executor",
        DisplayName: "Default Executor",
        Description: "Standard Atelier drafting executor: clear, concise, briefing-bound prose; revises iterations against reviewer findings.",
        SystemPrompt: SystemPrompts.Executor,
        Provider: "openrouter",
        Model: "anthropic/claude-opus-4.7",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Briefing-fidelity reviewer — checks the draft against briefing requirements (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile BriefingFidelityProfile = new(
        Name: "briefing-fidelity",
        DisplayName: "Briefing Fidelity",
        Description: "Verifies that the draft fully addresses every briefing requirement. Outside-model perspective for genuine independence from the executor.",
        SystemPrompt: SystemPrompts.BriefingFidelity,
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Clarity reviewer — checks argumentation, structure, and style (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile ClarityProfile = new(
        Name: "clarity",
        DisplayName: "Clarity",
        Description: "Audits clarity, argumentation, structure, and style. Outside-model perspective; complements the briefing-fidelity reviewer with a different model family.",
        SystemPrompt: SystemPrompts.Clarity,
        Provider: "openrouter",
        Model: "openai/gpt-4o-mini",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>The only system template in PS-5: the Klassik crew that reproduces the PS-2 hardcoded behaviour.</summary>
    public static readonly CrewTemplate KlassikTemplate = new(
        Name: KlassikTemplateName,
        DisplayName: "Klassik",
        Description: "The default Atelier crew: one drafting executor plus briefing-fidelity and clarity reviewers running in parallel. Reproduces the pre-PS-5 hardcoded pipeline.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, ClarityProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: Array.Empty<string>(),
        GroundingProviderNames: Array.Empty<string>(),
        IsSystem: true);

    /// <summary>System grounding-provider profile for Tavily Basic web-search (1 credit/search, ~5 sources).</summary>
    public static readonly GroundingProviderProfile TavilyBasicProfile = new(
        Name: "tavily-basic",
        DisplayName: "Tavily Basic Web Search",
        Description: "Web research via the Tavily API (Basic tier, 1 credit per search). Returns ~5 web sources with title, URL and snippet.",
        ProviderType: "tavily",
        ProviderSettings: new Dictionary<string, string>
        {
            ["Tier"] = "basic",
            ["MaxResults"] = "5",
            ["IncludeAnswer"] = "true",
        },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>System grounding-provider profile for the full knowledge base (vector-store, Top-5 results, no tag filter).</summary>
    public static readonly GroundingProviderProfile KnowledgeBaseDefaultProfile = new(
        Name: "knowledge-base-default",
        DisplayName: "Knowledge Base (All Documents)",
        Description: "Searches the full knowledge base for briefing-relevant passages using semantic similarity. Returns the top 5 most similar chunks.",
        ProviderType: "vector-store",
        ProviderSettings: new Dictionary<string, string> { ["TopK"] = "5" },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>All system reviewer profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> ReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [BriefingFidelityProfile.Name] = BriefingFidelityProfile,
            [ClarityProfile.Name] = ClarityProfile,
        };

    /// <summary>All system executor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> ExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DefaultExecutorProfile.Name] = DefaultExecutorProfile,
        };

    /// <summary>Strategic advisor consulted once before the executor begins: surfaces unclear constraints and missing context.</summary>
    public static readonly AdvisorProfile BriefingClarifierProfile = new(
        Name: "briefing-clarifier",
        DisplayName: "Briefing Clarifier",
        Description: "Strategic consultant. Analyzes briefings for unclear constraints, missing context, or unrealistic scope before the Executor begins.",
        SystemPrompt: "You are a strategic consultant reviewing a text briefing before an AI executor processes it. Identify up to 5 key strategic observations: unclear constraints, missing context, unrealistic scope, or conflicting requirements. Be concise (2-3 sentences per point). Do NOT write the text yourself — advise the executor.",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash-preview",
        MaxTokens: null,
        Mode: AdvisorMode.Strategic,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: true);

    /// <summary>Adversarial advisor consulted before every iteration: challenges weak assumptions in the current draft.</summary>
    public static readonly AdvisorProfile DevilsAdvocateProfile = new(
        Name: "devils-advocate",
        DisplayName: "Devil's Advocate",
        Description: "Adversarial perspective. After each iteration, challenges the strongest claims of the artifact to surface weak assumptions.",
        SystemPrompt: "You are a critical reviewer tasked with challenging an AI-generated text artifact. In 2-4 sentences, identify the weakest assumptions or most contestable claims. Be constructive — aim to strengthen the final text, not tear it down. Do NOT rewrite the text.",
        Provider: "openrouter",
        Model: "openai/gpt-4o-mini",
        MaxTokens: null,
        Mode: AdvisorMode.DevilsAdvocate,
        Trigger: AdvisorTrigger.BeforeEveryExecution,
        IsSystem: true);

    /// <summary>All system advisor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, AdvisorProfile> AdvisorProfiles =
        new Dictionary<string, AdvisorProfile>
        {
            [BriefingClarifierProfile.Name] = BriefingClarifierProfile,
            [DevilsAdvocateProfile.Name] = DevilsAdvocateProfile,
        };

    /// <summary>All system grounding-provider profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, GroundingProviderProfile> GroundingProviderProfiles =
        new Dictionary<string, GroundingProviderProfile>
        {
            [TavilyBasicProfile.Name] = TavilyBasicProfile,
            [KnowledgeBaseDefaultProfile.Name] = KnowledgeBaseDefaultProfile,
        };

    /// <summary>All system crew templates, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CrewTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [KlassikTemplate.Name] = KlassikTemplate,
        };

    /// <summary>True when the supplied name matches a system profile or template (any kind).</summary>
    public static bool IsSystemName(string name) =>
        ReviewerProfiles.ContainsKey(name)
        || ExecutorProfiles.ContainsKey(name)
        || CrewTemplates.ContainsKey(name);

    /// <summary>True when the supplied name matches a system advisor profile.</summary>
    public static bool IsSystemAdvisorName(string name) =>
        AdvisorProfiles.ContainsKey(name);

    /// <summary>True when the supplied name matches a system grounding-provider profile.</summary>
    public static bool IsSystemGroundingProviderName(string name) =>
        GroundingProviderProfiles.ContainsKey(name);

    /// <summary>Ensures the supplied name carries the <c>"custom-"</c> prefix; idempotent.</summary>
    public static string EnsureCustomPrefix(string name) =>
        name.StartsWith(CustomPrefix, StringComparison.Ordinal) ? name : CustomPrefix + name;
}
