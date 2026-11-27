namespace Geef.Atelier.Core.Domain.Providers;

/// <summary>
/// Well-known string keys used in <see cref="Provider.Settings"/>.
/// Centralises key names so callers never hard-code raw strings.
/// </summary>
public static class ProviderSettingsKeys
{
    /// <summary>
    /// Boolean flag overriding whether a provider supports OpenAI <c>response_format</c> structured
    /// outputs (json_object / json_schema). When absent the default is derived from
    /// <see cref="Provider.Type"/> (Http/Cli → true).
    /// </summary>
    public const string SupportsStructuredOutputs = "supportsStructuredOutputs";
}
