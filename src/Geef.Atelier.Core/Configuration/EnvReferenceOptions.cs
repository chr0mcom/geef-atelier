namespace Geef.Atelier.Core.Configuration;

/// <summary>
/// Allow list for environment-variable names that user-editable configuration may reference (provider
/// <c>ApiKeyEnv</c>/<c>EndpointEnvOverride</c>). Entries are exact names or prefixes ending in <c>*</c>.
/// The effective list is <see cref="DefaultAllowed"/> plus <see cref="Allowed"/>.
/// </summary>
public sealed class EnvReferenceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "EnvReference";

    /// <summary>
    /// Names in use on 2026-11-24 (system providers, system tools, production profiles, compose key names) plus the
    /// <c>ATELIER_SECRET_*</c> prefix for new secrets. Infrastructure secrets (database, MCP token, cli-proxy key,
    /// admin password hash) are deliberately absent.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowed =
    [
        "LLM_OPENROUTER_API_KEY",
        "OPENROUTER_API_KEY",
        "OPENAI_API_KEY",
        "GEMINI_API_KEY",
        "GROQ_API_KEY",
        "DEEPSEEK_API_KEY",
        "AZURE_OPENAI_API_KEY",
        "XAI_API_KEY",
        "OLLAMA_ENDPOINT",
        "TAVILY_API_KEY",
        "ATELIER_SECRET_*",
    ];

    /// <summary>Additional allowed names or prefixes (ending in <c>*</c>) from configuration.</summary>
    public List<string> Allowed { get; set; } = [];
}
