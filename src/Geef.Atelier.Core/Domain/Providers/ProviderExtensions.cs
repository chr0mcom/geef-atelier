namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>Extension methods on <see cref="Provider"/>.</summary>
public static class ProviderExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> if this provider supports agentic tool use (multi-turn tool calls).
    /// <para>
    /// The flag is read from <see cref="ProviderSettingsKeys.SupportsAgenticTools"/> in
    /// <see cref="Provider.Settings"/>.  When absent the default is derived from
    /// <see cref="Provider.Type"/>:
    /// <list type="bullet">
    ///   <item><see cref="ProviderType.Http"/> — <see langword="true"/> (OpenAI-compatible function calling)</item>
    ///   <item><see cref="ProviderType.Cli"/>  — <see langword="true"/> (CLI proxy extended in A-T6)</item>
    ///   <item>Any future type          — <see langword="false"/> unless explicitly enabled</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool SupportsAgenticTools(this Provider provider)
    {
        if (provider.Settings.TryGetValue(ProviderSettingsKeys.SupportsAgenticTools, out var val))
        {
            if (val.ValueKind == JsonValueKind.False)
                return false;

            if (val.ValueKind == JsonValueKind.True)
                return true;

            if (val.ValueKind == JsonValueKind.String)
            {
                var s = val.GetString();
                return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Default by provider type.
        return provider.Type is ProviderType.Http or ProviderType.Cli;
    }
}
