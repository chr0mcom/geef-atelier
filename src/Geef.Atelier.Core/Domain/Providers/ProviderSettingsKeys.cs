namespace Geef.Atelier.Core.Domain.Providers;

/// <summary>
/// Well-known string keys used in <see cref="Provider.Settings"/>.
/// Centralises key names so callers never hard-code raw strings.
/// </summary>
public static class ProviderSettingsKeys
{
    /// <summary>
    /// Boolean flag (JSON <c>true</c>/<c>false</c> or string <c>"true"</c>/<c>"false"</c>) that
    /// explicitly overrides whether a provider supports agentic tool use (multi-turn tool calls).
    /// When absent the default is derived from <see cref="Provider.Type"/>.
    /// </summary>
    public const string SupportsAgenticTools = "supportsAgenticTools";
}
