namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>Extension methods on <see cref="Provider"/>.</summary>
public static class ProviderExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> if this provider supports OpenAI <c>response_format</c>
    /// structured outputs. Read from <see cref="ProviderSettingsKeys.SupportsStructuredOutputs"/>;
    /// when absent, defaults to <see langword="true"/> for <see cref="ProviderType.Http"/> and
    /// <see cref="ProviderType.Cli"/> (the cli-proxy validates json_schema server-side).
    /// </summary>
    public static bool SupportsStructuredOutputs(this Provider provider)
    {
        if (provider.Settings.TryGetValue(ProviderSettingsKeys.SupportsStructuredOutputs, out var val))
        {
            if (val.ValueKind == JsonValueKind.False)
                return false;
            if (val.ValueKind == JsonValueKind.True)
                return true;
            if (val.ValueKind == JsonValueKind.String)
                return string.Equals(val.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        return provider.Type is ProviderType.Http or ProviderType.Cli;
    }
}
