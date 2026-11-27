using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Read-only catalogue of system-defined crew profiles and templates. These constants are
/// versioned with the Atelier source and ship as immutable defaults; user-editable variants
/// live in the database under the <c>"custom-"</c> name prefix.
/// </summary>
/// <remarks>
/// <para>Models are never literals here: every LLM role carries a <see cref="ModelSlot"/> and takes its displayed
/// binding from <see cref="ModelMatrix"/>; the concrete model is assigned per run at snapshot-build time
/// (D-066). Shared generic profiles carry no profile domain and inherit the crew's <see cref="CrewTemplate.Domain"/>.</para>
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
        Provider: ModelMatrix.Primary(ModelSlot.Executor).Provider,
        Model: ModelMatrix.Primary(ModelSlot.Executor).Model,
        MaxTokens: 256000,
        IsSystem: true,
        ModelSlot: ModelSlot.Executor);

    /// <summary>Briefing-fidelity reviewer — checks the draft against briefing requirements (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile BriefingFidelityProfile = new(
        Name: "briefing-fidelity",
        DisplayName: "Briefing Fidelity",
        Description: "Verifies that the draft fully addresses every briefing requirement. Outside-model perspective for genuine independence from the executor.",
        SystemPrompt: SystemPrompts.BriefingFidelity,
        Provider: ModelMatrix.Primary(ModelSlot.FactReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.FactReviewer).Model,
        MaxTokens: 64000,
        IsSystem: true,
        ModelSlot: ModelSlot.FactReviewer);

    /// <summary>Clarity reviewer — checks argumentation, structure, and style (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile ClarityProfile = new(
        Name: "clarity",
        DisplayName: "Clarity",
        Description: "Audits clarity, argumentation, structure, and style. Outside-model perspective; complements the briefing-fidelity reviewer with a different model family.",
        SystemPrompt: SystemPrompts.Clarity,
        Provider: ModelMatrix.Primary(ModelSlot.StyleReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.StyleReviewer).Model,
        MaxTokens: 64000,
        IsSystem: true,
        ModelSlot: ModelSlot.StyleReviewer);

    // ── Generic domain reviewer profiles ────────────────────────────────────────────
    // Both carry a generic role prompt with a {specialization} slot. Domain specifics are supplied
    // per crew via bound SpecializationPacks (see SystemPacks + the domain templates below), so the
    // same generic reviewer is safely reusable across legal / academic / marketing crews.

    /// <summary>Generic terminology/convention reviewer — specialised per crew via a bound pack.</summary>
    public static readonly ReviewerProfile DomainTerminologyReviewerProfile = new(
        Name: "domain-terminology-reviewer",
        DisplayName: "Domain Terminology Reviewer",
        Description: "Generic reviewer for precise, consistent domain terminology and field conventions. Specialised per crew via a bound specialization pack (e.g. legal-terminology, academic-citation, marketing-voice).",
        SystemPrompt: SystemPrompts.DomainTerminologyReviewer,
        Provider: ModelMatrix.Primary(ModelSlot.StyleReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.StyleReviewer).Model,
        MaxTokens: 64000,
        IsSystem: true,
        ModelSlot: ModelSlot.StyleReviewer);

    /// <summary>Generic substantive-rigor reviewer — specialised per crew via a bound pack.</summary>
    public static readonly ReviewerProfile SubstantiveRigorReviewerProfile = new(
        Name: "substantive-rigor-reviewer",
        DisplayName: "Substantive Rigor Reviewer",
        Description: "Generic reviewer for substantive soundness: logical validity, claim safety, and adequacy of support. Specialised per crew via a bound specialization pack (e.g. legal-clause-risk, academic-argumentation, marketing-conversion).",
        SystemPrompt: SystemPrompts.SubstantiveRigorReviewer,
        Provider: ModelMatrix.Primary(ModelSlot.DomainReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.DomainReviewer).Model,
        MaxTokens: 64000,
        IsSystem: true,
        ModelSlot: ModelSlot.DomainReviewer);

    /// <summary>The only system template in PS-5: the Klassik crew that reproduces the PS-2 hardcoded behaviour.</summary>
    public static readonly CrewTemplate KlassikTemplate = new(
        Name: KlassikTemplateName,
        DisplayName: "Classic",
        Description: "The default Atelier crew: one drafting executor plus briefing-fidelity and clarity reviewers running in parallel. Reproduces the pre-PS-5 hardcoded pipeline.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, ClarityProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: Array.Empty<string>(),
        GroundingProviderNames: new[] { "tavily-refined" },
        FinalizerProfileNames: Array.Empty<string>(),
        IsSystem: true,
        Domain: ModelDomain.General);

    // ── Domain templates ────────────────────────────────────────────────────────────

    /// <summary>Juristisch template — for German legal texts: contract drafting, clause analysis, legal opinions.</summary>
    public static readonly CrewTemplate JuristischTemplate = new(
        Name: "juristisch",
        DisplayName: "Legal",
        Description: "For legal texts: contract drafts, clause analyses, legal opinions. With a domain-terminology review and a clause-risk check. Reviewers run sequentially, since the clause-risk check builds on the terminology output.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, DomainTerminologyReviewerProfile.Name, SubstantiveRigorReviewerProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Sequential,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 12, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
        AdvisorProfileNames: new[] { "legal-domain-expert" },
        GroundingProviderNames: new[] { "tavily-refined" },
        FinalizerProfileNames: Array.Empty<string>(),
        IsSystem: true,
        ActorPackBindings: new Dictionary<string, IReadOnlyList<string>>
        {
            ["reviewer:domain-terminology-reviewer"] = new[] { Specialization.SystemPacks.LegalTerminology.Name },
            ["reviewer:substantive-rigor-reviewer"]  = new[] { Specialization.SystemPacks.LegalClauseRisk.Name },
        },
        Domain: ModelDomain.Legal);

    /// <summary>Akademisch template — for scientific texts: papers, argumentation essays, research texts.</summary>
    public static readonly CrewTemplate AkademischTemplate = new(
        Name: "akademisch",
        DisplayName: "Academic",
        Description: "For academic texts: papers, argumentation essays, research texts. With a citation-readiness check and an argumentation-rigor review. Reviewers run sequentially.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, DomainTerminologyReviewerProfile.Name, SubstantiveRigorReviewerProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Sequential,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 8, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
        AdvisorProfileNames: new[] { "academic-rigor-advisor" },
        GroundingProviderNames: new[] { "tavily-refined" },
        FinalizerProfileNames: Array.Empty<string>(),
        IsSystem: true,
        ActorPackBindings: new Dictionary<string, IReadOnlyList<string>>
        {
            ["reviewer:domain-terminology-reviewer"] = new[] { Specialization.SystemPacks.AcademicCitation.Name },
            ["reviewer:substantive-rigor-reviewer"]  = new[] { Specialization.SystemPacks.AcademicArgumentation.Name },
        },
        Domain: ModelDomain.Academic);

    /// <summary>Marketing template — for marketing copy: landing pages, emails, ad copy.</summary>
    public static readonly CrewTemplate MarketingTemplate = new(
        Name: "marketing",
        DisplayName: "Marketing",
        Description: "For marketing copy: landing pages, emails, ad copy. With an audience-clarity check and a conversion-strength review. Reviewers run in parallel (independent perspectives).",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, DomainTerminologyReviewerProfile.Name, SubstantiveRigorReviewerProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: Array.Empty<string>(),
        GroundingProviderNames: new[] { "tavily-refined" },
        FinalizerProfileNames: Array.Empty<string>(),
        IsSystem: true,
        ActorPackBindings: new Dictionary<string, IReadOnlyList<string>>
        {
            ["reviewer:domain-terminology-reviewer"] = new[] { Specialization.SystemPacks.MarketingVoice.Name },
            ["reviewer:substantive-rigor-reviewer"]  = new[] { Specialization.SystemPacks.MarketingConversion.Name },
        },
        Domain: ModelDomain.Marketing);

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
            [GroundingProviderProfile.KeyRefinementProvider]    = ModelMatrix.Primary(ModelSlot.GroundingRefiner).Provider,
            [GroundingProviderProfile.KeyRefinementModel]       = ModelMatrix.Primary(ModelSlot.GroundingRefiner).Model,
            [GroundingProviderProfile.KeyModelSlot] = nameof(ModelSlot.GroundingRefiner),
            [GroundingProviderProfile.KeyRefinementMaxTokens]   = "2048",
            [GroundingProviderProfile.KeyRefinementMode]        = "0",
        },
        IsSystem: true);

    /// <summary>All system reviewer profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> ReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [BriefingFidelityProfile.Name]                  = BriefingFidelityProfile,
            [ClarityProfile.Name]                           = ClarityProfile,
            [DomainTerminologyReviewerProfile.Name]         = DomainTerminologyReviewerProfile,
            [SubstantiveRigorReviewerProfile.Name]          = SubstantiveRigorReviewerProfile,
        };

    /// <summary>All system executor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> ExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DefaultExecutorProfile.Name]         = DefaultExecutorProfile,
        };

    // ── Domain advisor profiles ─────────────────────────────────────────────────────

    /// <summary>Legal domain expert advisor — pre-checks briefings for legal practicability (BeforeFirstExecution).</summary>
    public static readonly AdvisorProfile LegalDomainExpertProfile = new(
        Name: "legal-domain-expert",
        DisplayName: "Legal Domain Expert",
        Description: "Pre-checks briefings for legal practicability before drafting begins. Identifies constraints, terminological traps, regulatory context, missing information, and risk areas where qualifications are needed.",
        SystemPrompt: SystemPrompts.LegalDomainExpert,
        Provider: ModelMatrix.Primary(ModelSlot.DomainExpertAdvisor, ModelDomain.Legal).Provider,
        Model: ModelMatrix.Primary(ModelSlot.DomainExpertAdvisor, ModelDomain.Legal).Model,
        MaxTokens: 64000,
        Mode: AdvisorMode.DomainExpert,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: true,
        ModelSlot: ModelSlot.DomainExpertAdvisor,
        ModelDomain: ModelDomain.Legal);

    /// <summary>Academic rigor advisor — challenges weakest assumptions in the draft before every iteration.</summary>
    public static readonly AdvisorProfile AcademicRigorAdvisorProfile = new(
        Name: "academic-rigor-advisor",
        DisplayName: "Academic Rigor Advisor",
        Description: "Challenges the weakest assumptions, contested claims, and methodological gaps in the current draft before each iteration. Rotates focus to prevent repetitive critique.",
        SystemPrompt: SystemPrompts.AcademicRigorAdvisor,
        Provider: ModelMatrix.Primary(ModelSlot.DomainExpertAdvisor, ModelDomain.Academic).Provider,
        Model: ModelMatrix.Primary(ModelSlot.DomainExpertAdvisor, ModelDomain.Academic).Model,
        MaxTokens: 64000,
        Mode: AdvisorMode.Critical,
        Trigger: AdvisorTrigger.BeforeEveryExecution,
        IsSystem: true,
        ModelSlot: ModelSlot.DomainExpertAdvisor,
        ModelDomain: ModelDomain.Academic);

    /// <summary>Strategic advisor consulted once before the executor begins: surfaces unclear constraints and missing context.</summary>
    public static readonly AdvisorProfile BriefingClarifierProfile = new(
        Name: "briefing-clarifier",
        DisplayName: "Briefing Clarifier",
        Description: "Strategic consultant. Analyzes briefings for unclear constraints, missing context, or unrealistic scope before the Executor begins.",
        SystemPrompt: "You are a strategic consultant reviewing a text briefing before an AI executor processes it. Identify up to 5 key strategic observations: unclear constraints, missing context, unrealistic scope, or conflicting requirements. Be concise (2-3 sentences per point). Do NOT write the text yourself — advise the executor.",
        Provider: ModelMatrix.Primary(ModelSlot.StrategicAdvisor).Provider,
        Model: ModelMatrix.Primary(ModelSlot.StrategicAdvisor).Model,
        MaxTokens: 64000,
        Mode: AdvisorMode.Strategic,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: true,
        ModelSlot: ModelSlot.StrategicAdvisor);

    /// <summary>Adversarial advisor consulted before every iteration: challenges weak assumptions in the current draft.</summary>
    public static readonly AdvisorProfile DevilsAdvocateProfile = new(
        Name: "devils-advocate",
        DisplayName: "Devil's Advocate",
        Description: "Adversarial perspective. After each iteration, challenges the strongest claims of the artifact to surface weak assumptions.",
        SystemPrompt: "You are a critical reviewer tasked with challenging an AI-generated text artifact. In 2-4 sentences, identify the weakest assumptions or most contestable claims. Be constructive — aim to strengthen the final text, not tear it down. Do NOT rewrite the text.",
        Provider: ModelMatrix.Primary(ModelSlot.CriticalAdvisor).Provider,
        Model: ModelMatrix.Primary(ModelSlot.CriticalAdvisor).Model,
        MaxTokens: 64000,
        Mode: AdvisorMode.DevilsAdvocate,
        Trigger: AdvisorTrigger.BeforeEveryExecution,
        IsSystem: true,
        ModelSlot: ModelSlot.CriticalAdvisor);

    /// <summary>All system advisor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, AdvisorProfile> AdvisorProfiles =
        new Dictionary<string, AdvisorProfile>
        {
            [LegalDomainExpertProfile.Name]      = LegalDomainExpertProfile,
            [AcademicRigorAdvisorProfile.Name]   = AcademicRigorAdvisorProfile,
            [BriefingClarifierProfile.Name]      = BriefingClarifierProfile,
            [DevilsAdvocateProfile.Name]         = DevilsAdvocateProfile,
        };

    /// <summary>All system grounding-provider profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, GroundingProviderProfile> GroundingProviderProfiles =
        new Dictionary<string, GroundingProviderProfile>
        {
            [TavilyBasicProfile.Name]               = TavilyBasicProfile,
            [TavilyRefinedProfile.Name]              = TavilyRefinedProfile,
        };

    /// <summary>All system crew templates, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CrewTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [KlassikTemplate.Name]        = KlassikTemplate,
            [JuristischTemplate.Name]     = JuristischTemplate,
            [AkademischTemplate.Name]     = AkademischTemplate,
            [MarketingTemplate.Name]      = MarketingTemplate,
        };

    /// <summary>
    /// Returns true if the name belongs to a built-in reviewer, executor, or crew template.
    /// Advisor profiles are covered by <see cref="IsSystemAdvisorName"/>.
    /// For grounding providers, use <see cref="IsSystemGroundingProviderName"/> instead.
    /// </summary>
    public static bool IsSystemName(string name) =>
        ReviewerProfiles.ContainsKey(name)
        || ExecutorProfiles.ContainsKey(name)
        || CrewTemplates.ContainsKey(name)
        || CodeOnlyReviewerProfiles.ContainsKey(name)
        || CodeOnlyExecutorProfiles.ContainsKey(name)
        || CodeOnlyTemplates.ContainsKey(name);

    /// <summary>True when the supplied name matches a system advisor profile.</summary>
    public static bool IsSystemAdvisorName(string name) =>
        AdvisorProfiles.ContainsKey(name);

    /// <summary>
    /// Names of system grounding-provider profiles whose type was retired in stage 8c (D-089). They are no longer offered, but their
    /// stored rows stay protected like every system profile: they cannot be renamed, changed or deleted.
    /// </summary>
    public static readonly IReadOnlySet<string> RetiredGroundingProviderNames =
        new HashSet<string>(["academic-default", "tavily-news"], StringComparer.Ordinal);

    /// <summary>True when the supplied name matches a system grounding-provider profile, including the retired ones.</summary>
    public static bool IsSystemGroundingProviderName(string name) =>
        GroundingProviderProfiles.ContainsKey(name) || name == OrderSourcesProfile.Name || RetiredGroundingProviderNames.Contains(name);

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

    // ─ Transform (6 profiles) ──────────────────────────────────────────────────

    public static readonly FinalizerProfile AntiAiVoiceProfile = new(
        Name: "anti-ai-voice",
        DisplayName: "Anti-AI-Voice Polish",
        Description: "Removes AI-typical phrasing patterns (hedge stacking, filler phrases, synthetic transitions) while preserving every factual claim and the author's intentional style.",
        FinalizerType: FinalizerType.Transform,
        Settings: new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = SystemPrompts.TransformAntiAiVoice,
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformRewrite),
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
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformRewrite),
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
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformRewrite).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformRewrite),
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
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformCondense).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformCondense).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformCondense),
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
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformCondense).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformCondense).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformCondense),
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
            [TransformSettings.KeyProvider] = ModelMatrix.Primary(ModelSlot.TransformCondense).Provider,
            [TransformSettings.KeyModel] = ModelMatrix.Primary(ModelSlot.TransformCondense).Model,
            [TransformSettings.KeyModelSlot] = nameof(ModelSlot.TransformCondense),
            [TransformSettings.KeyMaxTokens] = "60000",
        },
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
            // Transform
            [AntiAiVoiceProfile.Name]         = AntiAiVoiceProfile,
            [ToneFormalizationProfile.Name]   = ToneFormalizationProfile,
            [ToneCasualProfile.Name]          = ToneCasualProfile,
            [ExecutiveSummaryProfile.Name]    = ExecutiveSummaryProfile,
            [KeyTakeawaysProfile.Name]        = KeyTakeawaysProfile,
            [GlossaryProfile.Name]            = GlossaryProfile,
        };

    /// <summary>True when the supplied name matches a system finalizer profile, including the retired ones.</summary>
    public static bool IsSystemFinalizerName(string name) =>
        FinalizerProfiles.ContainsKey(name) || RetiredFinalizerNames.Contains(name);

    /// <summary>
    /// Names of system finalizer profiles whose type was retired in stage 8c (D-089). They are no longer offered, but their stored rows
    /// stay protected like every system profile: they cannot be renamed, changed or deleted.
    /// </summary>
    public static readonly IReadOnlySet<string> RetiredFinalizerNames =
        new HashSet<string>(["webhook-sink", "email-sink"], StringComparer.Ordinal);

    /// <summary>Ensures the supplied name carries the <c>"custom-"</c> prefix; idempotent.</summary>
    public static string EnsureCustomPrefix(string name) =>
        name.StartsWith(CustomPrefix, StringComparison.Ordinal) ? name : CustomPrefix + name;

    /// <summary>Name of the code-only template of the direct path.</summary>
    public const string DirectTemplateName = "direct";

    /// <summary>Name of the code-only template of "Extra careful" with an outline first (<c>Orders:CarefulOutline</c>, D-087).</summary>
    public const string DirectOutlineTemplateName = "direct-outline";

    /// <summary>True for the templates the direct path runs: <see cref="DirectTemplateName"/> and <see cref="DirectOutlineTemplateName"/>.</summary>
    public static bool IsDirectPathTemplate(string? name) =>
        string.Equals(name, DirectTemplateName, StringComparison.Ordinal) || string.Equals(name, DirectOutlineTemplateName, StringComparison.Ordinal);

    /// <summary>Writer of the direct path: one writing pass from the request and the attached files.</summary>
    public static readonly ExecutorProfile DirectWriterProfile = new(
        Name: "direct-writer",
        DisplayName: "Writer",
        Description: "Writes the requested text in one pass from the request and the attached files; never invents facts.",
        SystemPrompt: SystemPrompts.DirectWriter,
        Provider: ModelMatrix.Primary(ModelSlot.Executor).Provider,
        Model: ModelMatrix.Primary(ModelSlot.Executor).Model,
        MaxTokens: 256000,
        IsSystem: true,
        ModelSlot: ModelSlot.Executor);

    /// <summary>Checks without an AI model; the implementation is injected at pipeline build time, this entry only registers the profile.</summary>
    public static readonly ReviewerProfile DeterministicChecksProfile = new(
        Name: "deterministic-checks",
        DisplayName: "Automatic checks",
        Description: "Checks length, placeholders, forbidden patterns, required terms and language without an AI model. Injected at pipeline build time.",
        SystemPrompt: "",
        Provider: "",
        Model: "",
        MaxTokens: 0,
        IsSystem: true);

    /// <summary>
    /// Fact check of the direct path against the request, the answers, the profile and the files; the implementation and its
    /// versioned prompt are injected at pipeline build time, this entry only registers the profile and its model slot.
    /// </summary>
    public static readonly ReviewerProfile SourceFactCheckProfile = new(
        Name: "source-fact-check",
        DisplayName: "Fact check",
        Description: "Compares the facts in the text with the request, the answers, the profile and the files. Injected at pipeline build time.",
        SystemPrompt: "Source fact check -- injected at pipeline build time.",
        Provider: ModelMatrix.Primary(ModelSlot.FactReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.FactReviewer).Model,
        MaxTokens: 8192,
        IsSystem: true,
        ModelSlot: ModelSlot.FactReviewer);

    /// <summary>
    /// Check of "Extra careful" with an outline (D-087, repair): lists the explicit requirements of the request word for word and says
    /// whether the first version meets them; the implementation is injected at pipeline build time. Fixed to the standard model of the
    /// intake (another model than the writer), without a slot, so that the model assignment keeps it.
    /// </summary>
    public static readonly ReviewerProfile RequirementsCheckProfile = new(
        Name: "requirements-check",
        DisplayName: "Request check",
        Description: "Checks whether the first version meets every explicit requirement of the request. Injected at pipeline build time.",
        SystemPrompt: "Requirements check -- injected at pipeline build time.",
        Provider: ModelMatrix.Primary(ModelSlot.Intake).Provider,
        Model: ModelMatrix.Primary(ModelSlot.Intake).Model,
        MaxTokens: 8192,
        IsSystem: true);

    /// <summary>Passes the files attached to an order in full text (careful mode).</summary>
    public static readonly GroundingProviderProfile OrderSourcesProfile = new(
        Name: "order-sources",
        DisplayName: "Your files",
        Description: "Passes the files attached to the order in full text.",
        ProviderType: GroundingProviderTypes.OrderSources,
        ProviderSettings: new Dictionary<string, string>(),
        IsSystem: true);

    /// <summary>
    /// Code-only template of the direct path. Deliberately not part of <see cref="CrewTemplates"/>, so it never shows up
    /// in template lists, the classic form or the composer catalogue.
    /// </summary>
    public static readonly CrewTemplate DirectTemplate = new(
        Name: DirectTemplateName,
        DisplayName: "Direct",
        Description: "One writing pass with all files in full text, automatic checks without a model and a fact check by a model of another family, at most one correction.",
        ExecutorProfileName: DirectWriterProfile.Name,
        ReviewerProfileNames: [DeterministicChecksProfile.Name, SourceFactCheckProfile.Name],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 2, AbortOnCritical: false, DetectRegression: false, StagnationThreshold: 2),
        AdvisorProfileNames: [],
        GroundingProviderNames: [],
        IsSystem: true,
        FinalizerProfileNames: [],
        Domain: ModelDomain.General);

    /// <summary>
    /// Code-only template of "Extra careful" with an outline first (D-087): the direct template under its own name, so that the run
    /// records that one call planned sections with word budgets before the writing pass. Same writer, checks, fact check and passes, plus
    /// the check of the explicit requirements of the request after the first version (<see cref="RequirementsCheckProfile"/>, repair of D-087).
    /// </summary>
    public static readonly CrewTemplate DirectOutlineTemplate = DirectTemplate with
    {
        Name = DirectOutlineTemplateName,
        DisplayName = "Extra careful",
        Description = "Plans the text in sections with word budgets first, then one writing pass along that plan, with the same automatic checks and fact check as the direct path and a check that every explicit requirement of the request is met, followed by at most one correction.",
        ReviewerProfileNames = [DeterministicChecksProfile.Name, SourceFactCheckProfile.Name, RequirementsCheckProfile.Name],
    };

    /// <summary>Executor profiles that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> CodeOnlyExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DirectWriterProfile.Name] = DirectWriterProfile,
        };

    /// <summary>Reviewer profiles that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> CodeOnlyReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [DeterministicChecksProfile.Name] = DeterministicChecksProfile,
            [SourceFactCheckProfile.Name] = SourceFactCheckProfile,
            [RequirementsCheckProfile.Name] = RequirementsCheckProfile,
        };

    /// <summary>Crew templates that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CodeOnlyTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [DirectTemplate.Name] = DirectTemplate,
            [DirectOutlineTemplate.Name] = DirectOutlineTemplate,
        };
}
