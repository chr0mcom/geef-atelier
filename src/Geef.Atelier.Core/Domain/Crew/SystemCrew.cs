using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
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
/// Hadwiger-Nelson replay (PS-5 AC 10), fall back to <c>claude-opus-4-7</c> for all
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
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
        MaxTokens: 256000,
        IsSystem: true);

    /// <summary>Briefing-fidelity reviewer — checks the draft against briefing requirements (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile BriefingFidelityProfile = new(
        Name: "briefing-fidelity",
        DisplayName: "Briefing Fidelity",
        Description: "Verifies that the draft fully addresses every briefing requirement. Outside-model perspective for genuine independence from the executor.",
        SystemPrompt: SystemPrompts.BriefingFidelity,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Clarity reviewer — checks argumentation, structure, and style (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile ClarityProfile = new(
        Name: "clarity",
        DisplayName: "Clarity",
        Description: "Audits clarity, argumentation, structure, and style. Outside-model perspective; complements the briefing-fidelity reviewer with a different model family.",
        SystemPrompt: SystemPrompts.Clarity,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    // ── Domain reviewer profiles ────────────────────────────────────────────────────

    /// <summary>Legal jargon precision reviewer — checks German legal texts for terminological exactness.</summary>
    public static readonly ReviewerProfile LegalJargonPrecisionProfile = new(
        Name: "legal-jargon-precision",
        DisplayName: "Legal Jargon Precision",
        Description: "Checks German legal texts for precise use of legal terminology. Identifies colloquial substitutes where statutory terms are required (e.g., Anfechtung vs. Widerruf, Kündigung vs. Rücktritt).",
        SystemPrompt: SystemPrompts.LegalJargonPrecision,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Legal clause risk reviewer — identifies problematic or unenforceable contract clauses.</summary>
    public static readonly ReviewerProfile LegalClauseRiskProfile = new(
        Name: "legal-clause-risk",
        DisplayName: "Legal Clause Risk",
        Description: "Identifies problematic or void contract clauses. Checks AGB conformity (§307 BGB), consumer protection compliance (§§312ff./474ff. BGB), and penalty clause proportionality.",
        SystemPrompt: SystemPrompts.LegalClauseRisk,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Academic citation readiness reviewer — marks uncited claims that require sources.</summary>
    public static readonly ReviewerProfile AcademicCitationReadinessProfile = new(
        Name: "academic-citation-readiness",
        DisplayName: "Academic Citation Readiness",
        Description: "Checks scholarly texts for citation adequacy. Distinguishes common knowledge from claims requiring attribution; marks uncited empirical findings and contested theoretical positions.",
        SystemPrompt: SystemPrompts.AcademicCitationReadiness,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Academic argumentation rigor reviewer — checks logical structure and identifies fallacies.</summary>
    public static readonly ReviewerProfile AcademicArgumentationRigorProfile = new(
        Name: "academic-argumentation-rigor",
        DisplayName: "Academic Argumentation Rigor",
        Description: "Checks academic argumentation for logical soundness. Maps Claim→Premise→Warrant→Conclusion and identifies non sequiturs, false dichotomies, hasty generalisations, and other formal fallacies.",
        SystemPrompt: SystemPrompts.AcademicArgumentationRigor,
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Marketing audience clarity reviewer — checks if copy matches the defined target audience.</summary>
    public static readonly ReviewerProfile MarketingAudienceClarityProfile = new(
        Name: "marketing-audience-clarity",
        DisplayName: "Marketing Audience Clarity",
        Description: "Checks marketing texts for target-audience alignment. Evaluates reading level, tone, jargon-accessibility balance, and cultural resonance against the stated audience persona.",
        SystemPrompt: SystemPrompts.MarketingAudienceClarity,
        Provider: "codex-cli",
        Model: "gpt-5.5",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Marketing conversion strength reviewer — checks CTAs, value propositions, and urgency signals.</summary>
    public static readonly ReviewerProfile MarketingConversionStrengthProfile = new(
        Name: "marketing-conversion-strength",
        DisplayName: "Marketing Conversion Strength",
        Description: "Checks marketing copy for conversion effectiveness. Verifies CTA clarity (action verb + outcome), USP visibility, urgency signals, and social proof integration.",
        SystemPrompt: SystemPrompts.MarketingConversionStrength,
        Provider: "codex-cli",
        Model: "gpt-5.5",
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
        GroundingProviderNames: new[] { "tavily-refined", "run-attachments", "learning-retriever-default" },
        FinalizerProfileNames: new[] { "learning-extractor" },
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
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 12, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
        AdvisorProfileNames: new[] { "legal-domain-expert" },
        GroundingProviderNames: new[] { "tavily-refined", "run-attachments", "learning-retriever-default" },
        FinalizerProfileNames: new[] { "learning-extractor" },
        IsSystem: true);

    /// <summary>Akademisch template — for scientific texts: papers, argumentation essays, research texts.</summary>
    public static readonly CrewTemplate AkademischTemplate = new(
        Name: "akademisch",
        DisplayName: "Akademisch",
        Description: "Für wissenschaftliche Texte: Papers, Argumentations-Aufsätze, Forschungstexte. Mit Zitierfähigkeits-Check und Argumentations-Stringenz-Review. Reviewers laufen sequenziell.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, AcademicCitationReadinessProfile.Name, AcademicArgumentationRigorProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Sequential,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 8, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
        AdvisorProfileNames: new[] { "academic-rigor-advisor" },
        GroundingProviderNames: new[] { "tavily-refined", "run-attachments", "learning-retriever-default" },
        FinalizerProfileNames: new[] { "learning-extractor" },
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
        GroundingProviderNames: new[] { "tavily-refined", "run-attachments", "learning-retriever-default" },
        FinalizerProfileNames: new[] { "learning-extractor" },
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
            ["MinRelevanceScore"] = "0.4",
            ["ExtractQuery"] = "true",
        },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>System grounding-provider profile for Tavily Advanced web-search with LLM-based citation filtering (1 credit/search, ~10 sources).</summary>
    public static readonly GroundingProviderProfile TavilyRefinedProfile = new(
        Name: "tavily-refined",
        DisplayName: "Tavily Web Search (AI-filtered)",
        Description: "Tavily web search with LLM-based citation filtering: irrelevant sources are automatically removed.",
        ProviderType: "tavily",
        ProviderSettings: new Dictionary<string, string>
        {
            ["Tier"]              = "advanced",
            ["MaxResults"]        = "10",
            ["IncludeAnswer"]     = "true",
            ["MinRelevanceScore"] = "0.3",
            ["ExtractQuery"]      = "true",
            [GroundingProviderProfile.KeyRefinementProvider]    = "deepseek",
            [GroundingProviderProfile.KeyRefinementModel]       = "deepseek/deepseek-v4-flash",
            [GroundingProviderProfile.KeyRefinementMaxTokens]   = "2048",
            [GroundingProviderProfile.KeyRefinementMode]        = "0",
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

    /// <summary>System grounding-provider profile for scientific papers via Semantic Scholar with LLM-based relevance filtering.</summary>
    public static readonly GroundingProviderProfile AcademicDefaultProfile = new(
        Name: "academic-default",
        DisplayName: "Academic Search (Semantic Scholar + AI-filtered)",
        Description: "Semantic Scholar search for scientific papers, filtered by an LLM refiner to retain only the most relevant results.",
        ProviderType: GroundingProviderTypes.AcademicSearch,
        ProviderSettings: new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyAcademicSource]             = "semantic-scholar",
            [GroundingProviderProfile.KeyAcademicMaxPapers]          = "5",
            [GroundingProviderProfile.KeyRefinementProvider]         = "google-ai-studio",
            [GroundingProviderProfile.KeyRefinementModel]            = "gemini-3.5-flash",
            [GroundingProviderProfile.KeyRefinementMaxTokens]        = "2048",
            [GroundingProviderProfile.KeyRefinementMode]             = "0",
        },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>System grounding-provider profile for recent news via Tavily news topic with LLM-based noise filtering.</summary>
    public static readonly GroundingProviderProfile TavilyNewsProfile = new(
        Name: "tavily-news",
        DisplayName: "Tavily News (recent + AI-filtered)",
        Description: "Recent news via Tavily news topic (last 7 days), filtered by an LLM refiner to remove noise.",
        ProviderType: GroundingProviderTypes.NewsSearch,
        ProviderSettings: new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRecencyDays]         = "7",
            [GroundingProviderProfile.KeyNewsMaxResults]      = "5",
            [GroundingProviderProfile.KeyNewsSearchDepth]     = "basic",
            [GroundingProviderProfile.KeyRefinementProvider]  = "deepseek",
            [GroundingProviderProfile.KeyRefinementModel]     = "deepseek/deepseek-v4-flash",
            [GroundingProviderProfile.KeyRefinementMaxTokens] = "2048",
            [GroundingProviderProfile.KeyRefinementMode]      = "0",
        },
        MaxQueriesPerRun: 1,
        IsSystem: true);

    /// <summary>
    /// System grounding-provider profile for the Continuous-Learning-Loop (D-054).
    /// Retrieves approved learnings from previous runs using cosine similarity with domain-aware boost.
    /// </summary>
    public static readonly GroundingProviderProfile LearningRetrieverDefaultProfile = new(
        Name: "learning-retriever-default",
        DisplayName: "Learning Retriever (domain-aware)",
        Description: "Retrieves approved learnings from the learning store using cosine similarity with a domain-boost. Place after curated knowledge providers.",
        ProviderType: GroundingProviderTypes.LearningRetrieval,
        ProviderSettings: new Dictionary<string, string>
        {
            ["sameDomainBoost"]    = "1.0",
            ["crossDomainPenalty"] = "0.5",
            ["maxLearnings"]       = "4",
        },
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
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
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
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
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
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
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
        Provider: "claude-cli",
        Model: "claude-opus-4-7",
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
            [TavilyBasicProfile.Name]               = TavilyBasicProfile,
            [TavilyRefinedProfile.Name]              = TavilyRefinedProfile,
            [KnowledgeBaseDefaultProfile.Name]       = KnowledgeBaseDefaultProfile,
            [RunAttachmentsProfile.Name]             = RunAttachmentsProfile,
            [TavilyNewsProfile.Name]                 = TavilyNewsProfile,
            [AcademicDefaultProfile.Name]            = AcademicDefaultProfile,
            [LearningRetrieverDefaultProfile.Name]   = LearningRetrieverDefaultProfile,
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

    // ── System Finalizer Profiles ──────────────────────────────────────────────────

    // ─ FileExport (6 profiles) ─────────────────────────────────────────────────

    public static readonly FinalizerProfile ExportMarkdownProfile = new(
        Name: "export-markdown",
        DisplayName: "Export: Markdown",
        Description: "Saves the final text as a Markdown (.md) file on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "markdown" },
        IsSystem: true);

    public static readonly FinalizerProfile ExportHtmlProfile = new(
        Name: "export-html",
        DisplayName: "Export: HTML",
        Description: "Converts the final Markdown to a self-contained HTML document and saves it on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "html" },
        IsSystem: true);

    public static readonly FinalizerProfile ExportPdfProfile = new(
        Name: "export-pdf",
        DisplayName: "Export: PDF",
        Description: "Converts the final Markdown to a PDF document (via QuestPDF) and saves it on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "pdf" },
        IsSystem: true);

    public static readonly FinalizerProfile ExportDocxProfile = new(
        Name: "export-docx",
        DisplayName: "Export: DOCX",
        Description: "Converts the final Markdown to a Word document (.docx) and saves it on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "docx" },
        IsSystem: true);

    public static readonly FinalizerProfile ExportTxtProfile = new(
        Name: "export-txt",
        DisplayName: "Export: Plain Text",
        Description: "Strips Markdown syntax and saves the final text as plain UTF-8 on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "txt" },
        IsSystem: true);

    public static readonly FinalizerProfile ExportJsonProfile = new(
        Name: "export-json",
        DisplayName: "Export: JSON",
        Description: "Wraps the final text in a JSON envelope (with run-id, template, and timestamp) and saves it on the export volume.",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "json" },
        IsSystem: true);

    // ─ MetadataEnrich (3 profiles) ─────────────────────────────────────────────

    public static readonly FinalizerProfile AddFrontMatterProfile = new(
        Name: "add-front-matter",
        DisplayName: "Add Front Matter",
        Description: "Prepends a YAML front-matter block (title, date, template, run-id) to the final Markdown text.",
        FinalizerType: FinalizerType.MetadataEnrich,
        Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = MetadataEnrichSettings.FrontMatter },
        IsSystem: true);

    public static readonly FinalizerProfile AddWordCountFooterProfile = new(
        Name: "add-word-count-footer",
        DisplayName: "Add Word-Count Footer",
        Description: "Appends a human-readable footer with the exact word count, character count, and estimated reading time.",
        FinalizerType: FinalizerType.MetadataEnrich,
        Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = MetadataEnrichSettings.WordCountFooter },
        IsSystem: true);

    public static readonly FinalizerProfile AddReadingLevelProfile = new(
        Name: "add-reading-level",
        DisplayName: "Add Reading Level",
        Description: "Computes a Flesch Reading Ease score and appends a brief readability summary to the final text.",
        FinalizerType: FinalizerType.MetadataEnrich,
        Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = MetadataEnrichSettings.ReadingLevel },
        IsSystem: true);

    // ─ ExternalSink (2 profiles) ───────────────────────────────────────────────

    public static readonly FinalizerProfile WebhookSinkProfile = new(
        Name: "webhook-sink",
        DisplayName: "Webhook Sink",
        Description: "POSTs the final text as JSON to a configured webhook URL. Customize URL and optional auth header in settings.",
        FinalizerType: FinalizerType.ExternalSink,
        Settings: new Dictionary<string, string>
        {
            [WebhookSinkSettings.KeySinkKind] = WebhookSinkSettings.SinkKindValue,
            [WebhookSinkSettings.KeyUrl] = "",
            [WebhookSinkSettings.KeyContentType] = "application/json",
            [WebhookSinkSettings.KeyTimeoutSeconds] = "30",
        },
        IsSystem: true);

    public static readonly FinalizerProfile EmailSinkProfile = new(
        Name: "email-sink",
        DisplayName: "E-Mail Sink",
        Description: "Sends the final text to a configured e-mail address via SMTP. SMTP credentials are resolved from environment variables at runtime.",
        FinalizerType: FinalizerType.ExternalSink,
        Settings: new Dictionary<string, string>
        {
            [EmailSinkSettings.KeySinkKind] = EmailSinkSettings.SinkKindValue,
            [EmailSinkSettings.KeyToAddress] = "",
            [EmailSinkSettings.KeySubject] = "Geef.Atelier — Run Result",
            [EmailSinkSettings.KeyAttachAsFile] = "false",
            [EmailSinkSettings.KeyAttachmentFormat] = "markdown",
        },
        IsSystem: true);

    // ─ Transform (6 profiles) ──────────────────────────────────────────────────

    public static readonly FinalizerProfile AntiAiVoiceProfile = new(
        Name: "anti-ai-voice",
        DisplayName: "Anti-AI-Voice Polish",
        Description: "Removes AI-typical phrasing patterns (hedge stacking, filler phrases, synthetic transitions) while preserving every factual claim and the author's intentional style.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformAntiAiVoice,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile ToneFormalizationProfile = new(
        Name: "tone-formalization",
        DisplayName: "Tone: Formalization",
        Description: "Shifts the register of the final text toward formal academic or professional prose without altering its content.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformToneFormalization,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile ToneCasualProfile = new(
        Name: "tone-casual",
        DisplayName: "Tone: Casual",
        Description: "Rewrites the final text in a conversational, approachable register suitable for blog posts or social content.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformToneCasual,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile ExecutiveSummaryProfile = new(
        Name: "executive-summary",
        DisplayName: "Executive Summary",
        Description: "Prepends a 3–5 sentence executive summary in the language of the text, then appends the full original below a horizontal rule.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformExecutiveSummary,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile KeyTakeawaysProfile = new(
        Name: "key-takeaways",
        DisplayName: "Key Takeaways",
        Description: "Appends a bulleted list of 5–7 key takeaways distilled from the final text.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformKeyTakeaways,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile GlossaryProfile = new(
        Name: "glossary",
        DisplayName: "Glossary",
        Description: "Identifies up to 10 domain-specific terms in the final text and appends brief, plain-language definitions as a glossary.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformGlossary,
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "60000",
        },
        IsSystem: true);

    public static readonly FinalizerProfile LearningExtractorProfile = new(
        Name: "learning-extractor",
        DisplayName: "Learning Extractor",
        Description: "Extracts structured learnings from a completed run and fires a gated learning-evaluation run.",
        FinalizerType: FinalizerType.LearningExtract,
        Settings: [],
        IsSystem: true);

    public static readonly FinalizerProfile LearningPublisherProfile = new(
        Name: "learning-publisher",
        DisplayName: "Learning Publisher",
        Description: "Publishes approved learning candidates to the learning store, or marks rejected ones.",
        FinalizerType: FinalizerType.LearningPublish,
        Settings: [],
        IsSystem: true);

    /// <summary>All system finalizer profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, FinalizerProfile> FinalizerProfiles =
        new Dictionary<string, FinalizerProfile>
        {
            // FileExport
            [ExportMarkdownProfile.Name]      = ExportMarkdownProfile,
            [ExportHtmlProfile.Name]          = ExportHtmlProfile,
            [ExportPdfProfile.Name]           = ExportPdfProfile,
            [ExportDocxProfile.Name]          = ExportDocxProfile,
            [ExportTxtProfile.Name]           = ExportTxtProfile,
            [ExportJsonProfile.Name]          = ExportJsonProfile,
            // MetadataEnrich
            [AddFrontMatterProfile.Name]      = AddFrontMatterProfile,
            [AddWordCountFooterProfile.Name]  = AddWordCountFooterProfile,
            [AddReadingLevelProfile.Name]     = AddReadingLevelProfile,
            // ExternalSink
            [WebhookSinkProfile.Name]         = WebhookSinkProfile,
            [EmailSinkProfile.Name]           = EmailSinkProfile,
            // Transform
            [AntiAiVoiceProfile.Name]         = AntiAiVoiceProfile,
            [ToneFormalizationProfile.Name]   = ToneFormalizationProfile,
            [ToneCasualProfile.Name]          = ToneCasualProfile,
            [ExecutiveSummaryProfile.Name]    = ExecutiveSummaryProfile,
            [KeyTakeawaysProfile.Name]        = KeyTakeawaysProfile,
            [GlossaryProfile.Name]            = GlossaryProfile,
            // Learning Loop
            [LearningExtractorProfile.Name]   = LearningExtractorProfile,
            [LearningPublisherProfile.Name]   = LearningPublisherProfile,
        };

    /// <summary>True when the supplied name matches a system finalizer profile.</summary>
    public static bool IsSystemFinalizerName(string name) =>
        FinalizerProfiles.ContainsKey(name);

    /// <summary>Ensures the supplied name carries the <c>"custom-"</c> prefix; idempotent.</summary>
    public static string EnsureCustomPrefix(string name) =>
        name.StartsWith(CustomPrefix, StringComparison.Ordinal) ? name : CustomPrefix + name;
}
