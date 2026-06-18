namespace Geef.Atelier.Web.Resources;

/// <summary>Field help texts displayed below each field in the Provider editor forms.</summary>
public static class ProviderFieldHelps
{
    public const string Name =
        "Unique identifier for this provider. Used internally and as a reference in profile forms. Lowercase letters, digits and hyphens only. Automatically prefixed with 'custom-'.";

    public const string DisplayName =
        "Display name in the UI, e.g. in the provider dropdown during profile creation.";

    public const string Description =
        "Description of what this provider is suited for — shown in the provider list.";

    public const string IsActive =
        "Inactive providers are not shown in new profile forms. Existing profiles that reference this provider continue to work.";

    // HTTP-specific
    public const string HttpEndpoint =
        "Base URL of the OpenAI-compatible endpoint, e.g. 'https://api.openai.com/v1'. No trailing slash.";

    public const string HttpApiKeyEnv =
        "Name of the environment variable that contains the API key, e.g. 'OPENAI_API_KEY'. The value is read from the server environment at runtime — never stored in the database.";

    public const string HttpEndpointEnvOverride =
        "Optional environment variable that overrides the endpoint URL. Useful for local instances like Ollama.";

    public const string HttpAuthHeaderName =
        "Name of the auth header. Default: 'Authorization'. For Azure OpenAI: 'api-key'.";

    public const string HttpAuthHeaderFormat =
        "Format of the auth header value with '{key}' as placeholder. Default: 'Bearer {key}'. For api-key auth: '{key}'.";

    public const string HttpModelsEndpoint =
        "Path to the models discovery endpoint relative to the base URL, e.g. '/models'. Leave empty if the provider has no models endpoint — then use a manual model list.";

    public const string HttpDefaultHeaders =
        "Optional additional HTTP headers sent with every request, e.g. HTTP-Referer or API version headers. Format: one header per line as 'Key: Value'.";

    public const string HttpManualModelList =
        "Manual model list when no models endpoint is available. One model per line.";

    public const string HttpCostPerInputToken =
        "Optional price per input token in EUR for cost tracking. Leave empty if unknown or not relevant.";

    public const string HttpCostPerOutputToken =
        "Optional price per output token in EUR for cost tracking. Leave empty if unknown or not relevant.";

    // CLI-specific
    public const string CliKind =
        "CLI type. For built-in CLIs (Claude, Codex, Gemini) most fields are pre-filled and read-only. For 'Generic' all fields are editable.";

    public const string CliMaxConcurrent =
        "Maximum number of concurrent CLI calls for this provider. Prevents rate limiting on subscription-based CLIs.";

    public const string CliBinary =
        "Path or name of the CLI binary, e.g. 'gemini' or '/usr/local/bin/mycli'.";

    public const string CliPromptArgsTemplate =
        "Argument template for the CLI call. '{prompt}' and '{model}' are replaced. Example: -p {prompt} --model {model} (one argument per line).";

    public const string CliStdinMode =
        "When enabled, the prompt is passed via stdin instead of as an argument.";

    public const string CliOutputFormat =
        "Format of the CLI output: text (free text), openai-json (OpenAI-compatible JSON), jsonl (streaming JSON, last event).";

    public const string CliOutputJsonPath =
        "JSON path to the response field in the output, e.g. 'response'. Only relevant for json/jsonl.";

    public const string CliModels =
        "Available models for this CLI provider. One per line, e.g. 'google/gemini-2.5-pro'.";
}
