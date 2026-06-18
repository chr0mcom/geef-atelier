namespace Geef.Atelier.Web.Resources;

/// <summary>Field help texts displayed below each field in the Studio Edit-Step.</summary>
public static class StudioFieldHelps
{
    // --- Template fields ---
    public const string DisplayName =
        "Display name of the template in the selection list. Short and concise, e.g. \"Legal contract reviewer\".";

    public const string Description =
        "Short description of what this crew template does and which tasks it is suited for.";

    public const string EvaluationStrategy =
        "How the reviewer results are combined. " +
        "Sequential: reviewers one after another, each can veto. " +
        "Parallel: all at once, the majority decides. " +
        "FailFast: execution stops at the first critical finding. " +
        "Priority: reviewers weighted by priority.";

    // --- Profile fields (all types) ---
    public const string ProfileName =
        "Internal identifier, unique in the system, lowercase letters and hyphens only (kebab-case), e.g. \"quality-reviewer\".";

    public const string ProfileDisplayName =
        "Display name of the profile in the selection list, short and concise.";

    public const string ProfileDescription =
        "Short description of the role this profile takes on and which tasks it is suited for.";

    public const string Provider =
        "The LLM provider this profile is connected through (e.g. OpenRouter, Anthropic, OpenAI).";

    public const string Model =
        "The AI model for this profile. Pick the provider first, then the matching model.";

    public const string MaxTokens =
        "Maximum response length in tokens. Reviewer: 2,048 is a good default. " +
        "Executor: 4,096 or more for longer texts.";

    public const string SystemPrompt =
        "The system prompt that controls this profile's behaviour. " +
        "The more concrete and focused, the better the results. Maximum 8,000 characters.";

    // --- Reviewer-specific ---
    public const string ReviewerFocus =
        "Optional focus hint on what the reviewer should pay particular attention to (e.g. \"stylistic confidence\" or \"factual accuracy\").";

    // --- Advisor-specific ---
    public const string AdvisorMode =
        "The strategic role of the advisor: " +
        "Strategic – big picture and risks. " +
        "Critical – weaknesses and counter-arguments. " +
        "DevilsAdvocate – actively argues against the plan.";

    public const string AdvisorTrigger =
        "When the advisor is used: " +
        "BeforeFirstExecution – once before the first executor run. " +
        "BeforeEveryExecution – before every executor run. " +
        "OnConvergenceFailure – only when convergence fails.";

    // --- Grounding-provider-specific ---
    public const string GroundingProviderType =
        "Type of knowledge source: Tavily for web search, VectorStore for your own documents.";

    public const string GroundingProviderSettings =
        "Type-specific settings. Tavily: api_key required. VectorStore: collection_name required.";

    public const string MaxQueriesPerRun =
        "How many search queries may be made per run at most (1–5).";

    // --- Finalizer-specific ---
    public const string FinalizerType =
        "Type of finalizer: " +
        "FileExport – exports the text to a file (Markdown, HTML, PDF, DOCX, TXT). " +
        "MetadataEnrich – enriches the text with metadata (front-matter, word count, readability). " +
        "ExternalSink – sends the text to a webhook or via email. " +
        "Transform – modifies the text through an AI model (e.g. anti-AI voice).";

    public const string FinalizerFileFormat =
        "Target file format: markdown, html, pdf, docx, txt. PDF and DOCX are generated server-side.";

    public const string FinalizerEnricherType =
        "Type of metadata enrichment: " +
        "front-matter – YAML header with title, creation time and word count. " +
        "word-count-footer – word count and reading time as a footer. " +
        "reading-level – Flesch-Kincaid reading level as a note in the text.";

    public const string FinalizerSinkType =
        "Target channel: webhook – HTTP POST to a URL. email – send via SMTP.";

    public const string FinalizerWebhookUrl =
        "Target URL for the webhook POST. The URL is not stored in logs.";

    public const string FinalizerWebhookAuthHeader =
        "Optional HTTP authorization header (e.g. \"Bearer my-token\"). " +
        "Is not displayed and not logged.";

    public const string FinalizerEmailTo =
        "Recipient email address. SMTP must be configured server-side (environment variables).";

    public const string FinalizerEmailSubject =
        "Subject line of the email. Supports placeholders: {run-id}, {template}, {timestamp}.";

    public const string FinalizerEmailAttach =
        "When enabled, the text is sent along as a file attachment (file name from the profile name).";

    public const string FinalizerTransformSystemPrompt =
        "Instruction for the AI transformation. Be concrete: what should be changed and what not. " +
        "Example: \"Rewrite the text in a natural, human voice. Avoid AI-typical sentence constructions.\" " +
        "Always ends with: 'Respond in the language of the input text.'";

    public const string FinalizerProfiles =
        "Finalizers run after the convergence step in the specified order. " +
        "Transform finalizers change the text; export and sink finalizers produce artifacts. " +
        "Order: transform first, then export.";

    public const string RunFinalizersOnMaxAttempts =
        "When enabled, the finalizers also run if convergence fails after the maximum iterations — " +
        "the last executor draft is then used as the result.";
}
