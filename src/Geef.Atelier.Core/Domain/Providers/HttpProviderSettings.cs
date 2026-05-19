namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>Typed wrapper over <see cref="Provider.Settings"/> for HTTP providers.</summary>
/// <param name="Endpoint">Base URL of the OpenAI-compatible API.</param>
/// <param name="ApiKeyEnv">Name of the environment variable holding the API key. Null when no key is required.</param>
/// <param name="EndpointEnvOverride">Optional env-var whose value overrides <see cref="Endpoint"/> at runtime.</param>
/// <param name="AuthHeaderName">HTTP header used for authentication (default: <c>Authorization</c>).</param>
/// <param name="AuthHeaderFormat">Format string for the header value; <c>{key}</c> is replaced with the key (default: <c>Bearer {key}</c>).</param>
/// <param name="ModelsEndpoint">Relative path to the models list endpoint, or null if not supported.</param>
/// <param name="DefaultHeaders">Additional HTTP headers always sent with every request.</param>
/// <param name="CostPerInputTokenEur">Informational cost per input token in EUR, stored as decimal string.</param>
/// <param name="CostPerOutputTokenEur">Informational cost per output token in EUR, stored as decimal string.</param>
/// <param name="ManualModelList">Explicit model list when the provider has no <see cref="ModelsEndpoint"/>.</param>
public sealed record HttpProviderSettings(
    string Endpoint,
    string? ApiKeyEnv,
    string? EndpointEnvOverride,
    string AuthHeaderName,
    string AuthHeaderFormat,
    string? ModelsEndpoint,
    Dictionary<string, string> DefaultHeaders,
    string? CostPerInputTokenEur,
    string? CostPerOutputTokenEur,
    IReadOnlyList<string>? ManualModelList
)
{
    /// <summary>Deserialises <see cref="Provider.Settings"/> into typed properties.</summary>
    public static HttpProviderSettings FromSettings(Dictionary<string, JsonElement> settings)
    {
        string Get(string key, string def = "") =>
            settings.TryGetValue(key, out var v) ? v.GetString() ?? def : def;
        string? GetNullable(string key) =>
            settings.TryGetValue(key, out var v) ? v.GetString() : null;

        var defaultHeaders = new Dictionary<string, string>();
        if (settings.TryGetValue("default_headers", out var hElem) && hElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in hElem.EnumerateObject())
                defaultHeaders[prop.Name] = prop.Value.GetString() ?? "";
        }

        List<string>? manualModels = null;
        if (settings.TryGetValue("models", out var mElem) && mElem.ValueKind == JsonValueKind.Array)
            manualModels = [.. mElem.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];

        return new HttpProviderSettings(
            Endpoint: Get("endpoint"),
            ApiKeyEnv: GetNullable("api_key_env"),
            EndpointEnvOverride: GetNullable("endpoint_env_override"),
            AuthHeaderName: Get("auth_header_name", "Authorization"),
            AuthHeaderFormat: Get("auth_header_format", "Bearer {key}"),
            ModelsEndpoint: GetNullable("models_endpoint"),
            DefaultHeaders: defaultHeaders,
            CostPerInputTokenEur: GetNullable("cost_per_input_token_eur"),
            CostPerOutputTokenEur: GetNullable("cost_per_output_token_eur"),
            ManualModelList: manualModels
        );
    }
}
