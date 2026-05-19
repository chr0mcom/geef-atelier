namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>Typed wrapper over <see cref="Provider.Settings"/> for CLI providers.</summary>
/// <param name="CliKind">Discriminator used to select the correct CLI adapter (e.g. <c>"claude"</c>, <c>"codex"</c>, <c>"gemini"</c>).</param>
/// <param name="Binary">Name or path of the CLI binary.</param>
/// <param name="AuthVolume">Docker volume path where auth state is persisted, or null.</param>
/// <param name="AuthCommand">Command to run for interactive authentication, or null.</param>
/// <param name="AuthEnvVarAlternative">Env-var that can substitute for the auth volume (e.g. for CI), or null.</param>
/// <param name="MaxConcurrent">Maximum number of parallel CLI invocations allowed.</param>
/// <param name="Models">Model identifiers supported by this CLI provider.</param>
/// <param name="PromptArgsTemplate">Argument template for passing the prompt to the binary, or null to use stdin.</param>
/// <param name="StdinMode">Whether the binary reads the prompt from stdin.</param>
/// <param name="OutputFormat">Expected output format (e.g. <c>"json"</c>, <c>"text"</c>), or null.</param>
/// <param name="OutputJsonPath">JSONPath to extract the response from structured CLI output, or null.</param>
/// <param name="ModelArgFlag">CLI flag used to pass the model name (e.g. <c>"--model"</c>), or null.</param>
/// <param name="AuthEnvVars">Additional env-vars injected into the CLI process for authentication.</param>
/// <param name="UseServerMode">Whether the CLI binary supports a long-running server mode.</param>
/// <param name="ServerEndpoint">Endpoint of the server-mode HTTP interface, or null.</param>
public sealed record CliProviderSettings(
    string CliKind,
    string Binary,
    string? AuthVolume,
    string? AuthCommand,
    string? AuthEnvVarAlternative,
    int MaxConcurrent,
    IReadOnlyList<string> Models,
    IReadOnlyList<string>? PromptArgsTemplate,
    bool? StdinMode,
    string? OutputFormat,
    string? OutputJsonPath,
    string? ModelArgFlag,
    Dictionary<string, string>? AuthEnvVars,
    bool? UseServerMode,
    string? ServerEndpoint
)
{
    /// <summary>Deserialises <see cref="Provider.Settings"/> into typed properties.</summary>
    public static CliProviderSettings FromSettings(Dictionary<string, JsonElement> settings)
    {
        string Get(string key, string def = "") =>
            settings.TryGetValue(key, out var v) ? v.GetString() ?? def : def;
        string? GetNullable(string key) =>
            settings.TryGetValue(key, out var v) ? v.GetString() : null;
        int GetInt(string key, int def) =>
            settings.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
        bool? GetBool(string key) =>
            settings.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean() : null;

        var models = new List<string>();
        if (settings.TryGetValue("models", out var mElem) && mElem.ValueKind == JsonValueKind.Array)
            models.AddRange(mElem.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));

        List<string>? promptArgs = null;
        if (settings.TryGetValue("prompt_args_template", out var paElem) && paElem.ValueKind == JsonValueKind.Array)
            promptArgs = [.. paElem.EnumerateArray().Select(e => e.GetString() ?? "")];

        Dictionary<string, string>? authEnvVars = null;
        if (settings.TryGetValue("auth_env_vars", out var aeElem) && aeElem.ValueKind == JsonValueKind.Object)
        {
            authEnvVars = [];
            foreach (var prop in aeElem.EnumerateObject())
                authEnvVars[prop.Name] = prop.Value.GetString() ?? "";
        }

        return new CliProviderSettings(
            CliKind: Get("cli_kind", "generic"),
            Binary: Get("binary"),
            AuthVolume: GetNullable("auth_volume"),
            AuthCommand: GetNullable("auth_command"),
            AuthEnvVarAlternative: GetNullable("auth_env_var_alternative"),
            MaxConcurrent: GetInt("max_concurrent", 2),
            Models: models,
            PromptArgsTemplate: promptArgs,
            StdinMode: GetBool("stdin_mode"),
            OutputFormat: GetNullable("output_format"),
            OutputJsonPath: GetNullable("output_json_path"),
            ModelArgFlag: GetNullable("model_arg_flag"),
            AuthEnvVars: authEnvVars,
            UseServerMode: GetBool("use_server_mode"),
            ServerEndpoint: GetNullable("server_endpoint")
        );
    }
}
