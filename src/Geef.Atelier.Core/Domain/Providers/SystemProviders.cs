namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>
/// Read-only catalogue of system-defined LLM providers. These constants are versioned with the
/// Atelier source and ship as immutable defaults. Custom providers live in the database under
/// the <c>"custom-"</c> name prefix.
/// </summary>
public static class SystemProviders
{
    /// <summary>Prefix automatically applied to user-created provider names to prevent collisions with system entries.</summary>
    public const string CustomPrefix = "custom-";

    // ── HTTP System Providers ─────────────────────────────────────────────────────────

    /// <summary>Pay-per-token aggregator with access to hundreds of models.</summary>
    public static readonly Provider OpenRouter = CreateHttpProvider(
        name: "openrouter",
        displayName: "OpenRouter",
        description: "Pay-per-token aggregator with access to hundreds of models from many providers. Standard choice when you don't have a direct subscription.",
        endpoint: "https://openrouter.ai/api/v1",
        apiKeyEnv: "LLM_OPENROUTER_API_KEY",
        modelsEndpoint: "/models",
        defaultHeaders: new() { ["HTTP-Referer"] = "https://geef.stefan-bechtel.de", ["X-Title"] = "Geef.Atelier" }
    );

    /// <summary>Direct connection to OpenAI without OpenRouter markup.</summary>
    public static readonly Provider OpenAiDirect = CreateHttpProvider(
        name: "openai-direct",
        displayName: "OpenAI (Direct)",
        description: "Direct connection to OpenAI's API. Use when you have an OpenAI API key and want no OpenRouter-markup. Recommended for production workloads with predictable cost.",
        endpoint: "https://api.openai.com/v1",
        apiKeyEnv: "OPENAI_API_KEY",
        modelsEndpoint: "/models"
    );

    /// <summary>Google Gemini via AI Studio's OpenAI-compatible endpoint.</summary>
    public static readonly Provider GoogleAiStudio = CreateHttpProvider(
        name: "google-ai-studio",
        displayName: "Google AI Studio (Gemini)",
        description: "Direct connection to Google's Gemini API via AI Studio. OpenAI-compatible endpoint.",
        endpoint: "https://generativelanguage.googleapis.com/v1beta/openai",
        apiKeyEnv: "GEMINI_API_KEY",
        modelsEndpoint: "/models"
    );

    /// <summary>Ultra-fast inference via Groq's LPU hardware.</summary>
    public static readonly Provider Groq = CreateHttpProvider(
        name: "groq",
        displayName: "Groq",
        description: "Ultra-fast inference for open-source models (Llama, Mixtral). OpenAI-compatible. Free tier available with rate limits. Good for high-throughput reviewers.",
        endpoint: "https://api.groq.com/openai/v1",
        apiKeyEnv: "GROQ_API_KEY",
        modelsEndpoint: "/models"
    );

    /// <summary>Affordable provider with strong coding and reasoning models.</summary>
    public static readonly Provider DeepSeek = CreateHttpProvider(
        name: "deepseek",
        displayName: "DeepSeek",
        description: "Affordable Chinese provider with strong coding and reasoning models. OpenAI-compatible. Often 10x cheaper than equivalent OpenAI models.",
        endpoint: "https://api.deepseek.com/v1",
        apiKeyEnv: "DEEPSEEK_API_KEY",
        modelsEndpoint: "/models"
    );

    /// <summary>Local Ollama instance with env-var endpoint override.</summary>
    public static readonly Provider OllamaLocal = CreateHttpProvider(
        name: "ollama-local",
        displayName: "Ollama (Local)",
        description: "Local Ollama instance for self-hosted models. No API key required. Set OLLAMA_ENDPOINT env-var to override default.",
        endpoint: "http://host.docker.internal:11434/v1",
        apiKeyEnv: null,
        modelsEndpoint: "/models",
        endpointEnvOverride: "OLLAMA_ENDPOINT",
        authHeaderName: null
    );

    /// <summary>Microsoft Azure OpenAI Service requiring a cloned custom provider for resource configuration.</summary>
    public static readonly Provider AzureOpenAi = CreateHttpProvider(
        name: "azure-openai",
        displayName: "Azure OpenAI",
        description: "Microsoft Azure OpenAI Service. Requires Azure subscription with OpenAI access. Clone as custom provider and configure resource_name and deployment_name.",
        endpoint: "https://{resource_name}.openai.azure.com/openai/deployments/{deployment_name}",
        apiKeyEnv: "AZURE_OPENAI_API_KEY",
        modelsEndpoint: null,
        authHeaderName: "api-key",
        authHeaderFormat: "{key}"
    );

    /// <summary>xAI Grok models via the OpenAI-compatible API.</summary>
    public static readonly Provider XAi = CreateHttpProvider(
        name: "xai",
        displayName: "xAI (Grok)",
        description: "xAI's Grok models via the OpenAI-compatible API. Requires an xAI API key. Models include Grok 3 and Grok 3 Mini.",
        endpoint: "https://api.x.ai/v1",
        apiKeyEnv: "XAI_API_KEY",
        modelsEndpoint: "/models"
    );

    /// <summary>Template for any other OpenAI-compatible endpoint.</summary>
    public static readonly Provider OpenAiCompatibleGeneric = CreateHttpProvider(
        name: "openai-compatible-generic",
        displayName: "OpenAI-Compatible (Generic)",
        description: "Template for any other OpenAI-compatible endpoint. Configure endpoint and API key. Use for Together AI, Fireworks, Mistral La Plateforme, local LiteLLM, or any OpenAI-compatible service.",
        endpoint: "",
        apiKeyEnv: "",
        modelsEndpoint: "/models"
    );

    // ── CLI System Providers ──────────────────────────────────────────────────────────

    /// <summary>Anthropic Claude via the official Claude Code CLI subscription.</summary>
    public static readonly Provider ClaudeCli = CreateCliProvider(
        name: "claude-cli",
        displayName: "Claude Code CLI",
        description: "Anthropic Claude via the official Claude Code CLI. Uses your Claude Pro/Max/Team subscription instead of paying per token.",
        cliKind: "claude",
        binary: "claude",
        authVolume: "/auth/claude",
        authCommand: "claude auth login",
        models: ["claude-opus-4-7", "claude-sonnet-4-6", "claude-haiku-4-5"]
    );

    /// <summary>OpenAI via the official Codex CLI subscription.</summary>
    public static readonly Provider CodexCli = CreateCliProvider(
        name: "codex-cli",
        displayName: "Codex CLI",
        description: "OpenAI via the official Codex CLI. Uses your ChatGPT Plus/Pro/Team subscription instead of paying per API token.",
        cliKind: "codex",
        binary: "codex",
        authVolume: "/auth/codex",
        authCommand: "codex auth login",
        models: ["gpt-5.5", "gpt-5.5-mini", "gpt-4o", "o1", "o3-mini"]
    );

    /// <summary>Google Gemini via the official Gemini CLI with free-tier quota.</summary>
    public static readonly Provider GeminiCli = CreateCliProvider(
        name: "gemini-cli",
        displayName: "Gemini CLI",
        description: "Google Gemini via the official Gemini CLI. Free tier offers 60 requests/minute and 1000 requests/day with a Google account login.",
        cliKind: "gemini",
        binary: "gemini",
        authVolume: "/auth/gemini",
        authCommand: "gemini",
        authEnvVarAlternative: "GEMINI_API_KEY",
        models: ["gemini-2-5-pro", "gemini-2-5-flash", "gemini-3-1-pro", "gemini-3-1-flash"]
    );

    // Note: opencode-cli is excluded pending Architect verification of install mechanism.
    // Users can add it as a custom CLI provider.

    /// <summary>All system providers indexed by their canonical name.</summary>
    public static readonly IReadOnlyDictionary<string, Provider> ProvidersByName =
        new Dictionary<string, Provider>
        {
            [OpenRouter.Name] = OpenRouter,
            [OpenAiDirect.Name] = OpenAiDirect,
            [GoogleAiStudio.Name] = GoogleAiStudio,
            [Groq.Name] = Groq,
            [DeepSeek.Name] = DeepSeek,
            [OllamaLocal.Name] = OllamaLocal,
            [AzureOpenAi.Name] = AzureOpenAi,
            [XAi.Name] = XAi,
            [OpenAiCompatibleGeneric.Name] = OpenAiCompatibleGeneric,
            [ClaudeCli.Name] = ClaudeCli,
            [CodexCli.Name] = CodexCli,
            [GeminiCli.Name] = GeminiCli,
        }.AsReadOnly();

    /// <summary>Returns true when <paramref name="name"/> matches a system provider.</summary>
    public static bool IsSystemProviderName(string name) => ProvidersByName.ContainsKey(name);

    /// <summary>Ensures the name carries the <c>"custom-"</c> prefix, adding it if absent.</summary>
    public static string EnsureCustomPrefix(string name) =>
        name.StartsWith(CustomPrefix, StringComparison.Ordinal) ? name : CustomPrefix + name;

    // ── Private factory helpers ───────────────────────────────────────────────────────

    private static Provider CreateHttpProvider(
        string name,
        string displayName,
        string description,
        string endpoint,
        string? apiKeyEnv,
        string? modelsEndpoint,
        Dictionary<string, string>? defaultHeaders = null,
        string? endpointEnvOverride = null,
        string? authHeaderName = "Authorization",
        string? authHeaderFormat = null,
        bool isActive = true)
    {
        var settings = new Dictionary<string, JsonElement>();

        void AddString(string key, string value) =>
            settings[key] = JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
        void AddNull(string key) =>
            settings[key] = JsonDocument.Parse("null").RootElement.Clone();

        AddString("endpoint", endpoint);
        if (apiKeyEnv is not null) AddString("api_key_env", apiKeyEnv); else AddNull("api_key_env");
        if (endpointEnvOverride is not null) AddString("endpoint_env_override", endpointEnvOverride);
        if (authHeaderName is not null) AddString("auth_header_name", authHeaderName);
        AddString("auth_header_format", authHeaderFormat ?? "Bearer {key}");
        if (modelsEndpoint is not null) AddString("models_endpoint", modelsEndpoint); else AddNull("models_endpoint");

        if (defaultHeaders is { Count: > 0 })
        {
            var headersJson = JsonSerializer.Serialize(defaultHeaders);
            settings["default_headers"] = JsonDocument.Parse(headersJson).RootElement.Clone();
        }

        var now = DateTimeOffset.UtcNow;
        return new Provider(name, displayName, description, ProviderType.Http, settings, IsSystem: true, IsActive: isActive, now, now);
    }

    private static Provider CreateCliProvider(
        string name,
        string displayName,
        string description,
        string cliKind,
        string binary,
        string? authVolume,
        string? authCommand,
        IReadOnlyList<string> models,
        string? authEnvVarAlternative = null,
        int maxConcurrent = 2,
        bool isActive = true)
    {
        var modelsJson = JsonSerializer.Serialize(models);
        var settings = new Dictionary<string, JsonElement>
        {
            ["cli_kind"] = JsonDocument.Parse(JsonSerializer.Serialize(cliKind)).RootElement.Clone(),
            ["binary"] = JsonDocument.Parse(JsonSerializer.Serialize(binary)).RootElement.Clone(),
            ["max_concurrent"] = JsonDocument.Parse(maxConcurrent.ToString()).RootElement.Clone(),
            ["models"] = JsonDocument.Parse(modelsJson).RootElement.Clone(),
        };

        void AddString(string key, string? value)
        {
            if (value is not null)
                settings[key] = JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
        }

        AddString("auth_volume", authVolume);
        AddString("auth_command", authCommand);
        AddString("auth_env_var_alternative", authEnvVarAlternative);

        var now = DateTimeOffset.UtcNow;
        return new Provider(name, displayName, description, ProviderType.Cli, settings, IsSystem: true, IsActive: isActive, now, now);
    }
}
