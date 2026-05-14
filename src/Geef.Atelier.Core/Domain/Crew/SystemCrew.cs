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

    // ── Domain reviewer profiles ────────────────────────────────────────────────────

    /// <summary>Legal jargon precision reviewer — checks German legal texts for terminological exactness.</summary>
    public static readonly ReviewerProfile LegalJargonPrecisionProfile = new(
        Name: "legal-jargon-precision",
        DisplayName: "Legal Jargon Precision",
        Description: "Checks German legal texts for precise use of legal terminology. Identifies colloquial substitutes where statutory terms are required (e.g., Anfechtung vs. Widerruf, Kündigung vs. Rücktritt).",
        SystemPrompt: SystemPrompts.LegalJargonPrecision,
        Provider: "openrouter",
        Model: "openai/gpt-4o-mini",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Legal clause risk reviewer — identifies problematic or unenforceable contract clauses.</summary>
    public static readonly ReviewerProfile LegalClauseRiskProfile = new(
        Name: "legal-clause-risk",
        DisplayName: "Legal Clause Risk",
        Description: "Identifies problematic or void contract clauses. Checks AGB conformity (§307 BGB), consumer protection compliance (§§312ff./474ff. BGB), and penalty clause proportionality.",
        SystemPrompt: SystemPrompts.LegalClauseRisk,
        Provider: "openrouter",
        Model: "anthropic/claude-sonnet-4-5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Academic citation readiness reviewer — marks uncited claims that require sources.</summary>
    public static readonly ReviewerProfile AcademicCitationReadinessProfile = new(
        Name: "academic-citation-readiness",
        DisplayName: "Academic Citation Readiness",
        Description: "Checks scholarly texts for citation adequacy. Distinguishes common knowledge from claims requiring attribution; marks uncited empirical findings and contested theoretical positions.",
        SystemPrompt: SystemPrompts.AcademicCitationReadiness,
        Provider: "openrouter",
        Model: "openai/gpt-4o-mini",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Academic argumentation rigor reviewer — checks logical structure and identifies fallacies.</summary>
    public static readonly ReviewerProfile AcademicArgumentationRigorProfile = new(
        Name: "academic-argumentation-rigor",
        DisplayName: "Academic Argumentation Rigor",
        Description: "Checks academic argumentation for logical soundness. Maps Claim→Premise→Warrant→Conclusion and identifies non sequiturs, false dichotomies, hasty generalisations, and other formal fallacies.",
        SystemPrompt: SystemPrompts.AcademicArgumentationRigor,
        Provider: "openrouter",
        Model: "anthropic/claude-sonnet-4-5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Marketing audience clarity reviewer — checks if copy matches the defined target audience.</summary>
    public static readonly ReviewerProfile MarketingAudienceClarityProfile = new(
        Name: "marketing-audience-clarity",
        DisplayName: "Marketing Audience Clarity",
        Description: "Checks marketing texts for target-audience alignment. Evaluates reading level, tone, jargon-accessibility balance, and cultural resonance against the stated audience persona.",
        SystemPrompt: SystemPrompts.MarketingAudienceClarity,
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Marketing conversion strength reviewer — checks CTAs, value propositions, and urgency signals.</summary>
    public static readonly ReviewerProfile MarketingConversionStrengthProfile = new(
        Name: "marketing-conversion-strength",
        DisplayName: "Marketing Conversion Strength",
        Description: "Checks marketing copy for conversion effectiveness. Verifies CTA clarity (action verb + outcome), USP visibility, urgency signals, and social proof integration.",
        SystemPrompt: SystemPrompts.MarketingConversionStrength,
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

    // ── Domain templates ────────────────────────────────────────────────────────────

    /// <summary>Juristisch template — for German legal texts: contract drafting, clause analysis, legal opinions.</summary>
    public static readonly CrewTemplate JuristischTemplate = new(
        Name: "juristisch",
        DisplayName: "Juristisch",
        Description: "Für juristische Texte: Vertragsentwürfe, Klausel-Analysen, rechtliche Stellungnahmen. Mit Fachterminologie-Review und Klausel-Risiko-Check. Reviewers laufen sequenziell, da Klausel-Risk auf dem Jargon-Output aufbaut.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, LegalJargonPrecisionProfile.Name, LegalClauseRiskProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Sequential,
        ConvergenceOverride: null,
        AdvisorProfileNames: new[] { "legal-domain-expert" },
        GroundingProviderNames: Array.Empty<string>(),
        IsSystem: true);

    /// <summary>Akademisch template — for scientific texts: papers, argumentation essays, research texts.</summary>
    public static readonly CrewTemplate AkademischTemplate = new(
        Name: "akademisch",
        DisplayName: "Akademisch",
        Description: "Für wissenschaftliche Texte: Papers, Argumentations-Aufsätze, Forschungstexte. Mit Zitierfähigkeits-Check und Argumentations-Stringenz-Review. Reviewers laufen sequenziell.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, AcademicCitationReadinessProfile.Name, AcademicArgumentationRigorProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Sequential,
        ConvergenceOverride: null,
        AdvisorProfileNames: new[] { "academic-rigor-advisor" },
        GroundingProviderNames: Array.Empty<string>(),
        IsSystem: true);

    /// <summary>Marketing template — for marketing copy: landing pages, emails, ad copy.</summary>
    public static readonly CrewTemplate MarketingTemplate = new(
        Name: "marketing",
        DisplayName: "Marketing",
        Description: "Für Marketing-Texte: Landing-Pages, Mails, Werbe-Copy. Mit Audience-Klarheits-Check und Conversion-Stärke-Review. Reviewers laufen parallel (unabhängige Perspektiven).",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, MarketingAudienceClarityProfile.Name, MarketingConversionStrengthProfile.Name },
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
        ProviderSettings: new Dictionary<string, string> { ["TopK"] = "5", ["Scope"] = "global" },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>System grounding-provider profile for run-local attachments uploaded with the briefing.</summary>
    public static readonly GroundingProviderProfile RunAttachmentsProfile = new(
        Name: "run-attachments",
        DisplayName: "Run Attachments",
        Description: "Uses documents uploaded with the briefing as a grounding source. Activated automatically when attachments are present.",
        ProviderType: "vector-store",
        ProviderSettings: new Dictionary<string, string> { ["TopK"] = "5", ["Scope"] = "run-local" },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>All system reviewer profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> ReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [BriefingFidelityProfile.Name]              = BriefingFidelityProfile,
            [ClarityProfile.Name]                       = ClarityProfile,
            [LegalJargonPrecisionProfile.Name]          = LegalJargonPrecisionProfile,
            [LegalClauseRiskProfile.Name]               = LegalClauseRiskProfile,
            [AcademicCitationReadinessProfile.Name]     = AcademicCitationReadinessProfile,
            [AcademicArgumentationRigorProfile.Name]    = AcademicArgumentationRigorProfile,
            [MarketingAudienceClarityProfile.Name]      = MarketingAudienceClarityProfile,
            [MarketingConversionStrengthProfile.Name]   = MarketingConversionStrengthProfile,
        };

    /// <summary>All system executor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> ExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DefaultExecutorProfile.Name] = DefaultExecutorProfile,
        };

    // ── Domain advisor profiles ─────────────────────────────────────────────────────

    /// <summary>Legal domain expert advisor — pre-checks briefings for legal practicability (BeforeFirstExecution).</summary>
    public static readonly AdvisorProfile LegalDomainExpertProfile = new(
        Name: "legal-domain-expert",
        DisplayName: "Legal Domain Expert",
        Description: "Pre-checks briefings for legal practicability before drafting begins. Identifies constraints, terminological traps, regulatory context, missing information, and risk areas where qualifications are needed.",
        SystemPrompt: SystemPrompts.LegalDomainExpert,
        Provider: "openrouter",
        Model: "anthropic/claude-sonnet-4-5",
        MaxTokens: null,
        Mode: AdvisorMode.DomainExpert,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: true);

    /// <summary>Academic rigor advisor — challenges weakest assumptions in the draft before every iteration.</summary>
    public static readonly AdvisorProfile AcademicRigorAdvisorProfile = new(
        Name: "academic-rigor-advisor",
        DisplayName: "Academic Rigor Advisor",
        Description: "Challenges the weakest assumptions, contested claims, and methodological gaps in the current draft before each iteration. Rotates focus to prevent repetitive critique.",
        SystemPrompt: SystemPrompts.AcademicRigorAdvisor,
        Provider: "openrouter",
        Model: "openai/gpt-4o-mini",
        MaxTokens: null,
        Mode: AdvisorMode.Critical,
        Trigger: AdvisorTrigger.BeforeEveryExecution,
        IsSystem: true);

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
            [LegalDomainExpertProfile.Name]      = LegalDomainExpertProfile,
            [AcademicRigorAdvisorProfile.Name]   = AcademicRigorAdvisorProfile,
            [BriefingClarifierProfile.Name]       = BriefingClarifierProfile,
            [DevilsAdvocateProfile.Name]          = DevilsAdvocateProfile,
        };

    /// <summary>All system grounding-provider profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, GroundingProviderProfile> GroundingProviderProfiles =
        new Dictionary<string, GroundingProviderProfile>
        {
            [TavilyBasicProfile.Name] = TavilyBasicProfile,
            [KnowledgeBaseDefaultProfile.Name] = KnowledgeBaseDefaultProfile,
            [RunAttachmentsProfile.Name] = RunAttachmentsProfile,
        };

    /// <summary>All system crew templates, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CrewTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [KlassikTemplate.Name]    = KlassikTemplate,
            [JuristischTemplate.Name] = JuristischTemplate,
            [AkademischTemplate.Name] = AkademischTemplate,
            [MarketingTemplate.Name]  = MarketingTemplate,
        };

    /// <summary>
    /// Returns true if the name belongs to a built-in reviewer, executor, or crew template.
    /// Advisor profiles are covered by <see cref="IsSystemAdvisorName"/>.
    /// For grounding providers, use <see cref="IsSystemGroundingProviderName"/> instead.
    /// </summary>
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
