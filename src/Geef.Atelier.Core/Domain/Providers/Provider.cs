namespace Geef.Atelier.Core.Domain.Providers;

using System.Text.Json;

/// <summary>
/// Describes an LLM provider entry — either an HTTP endpoint or a local CLI binary.
/// System providers are code constants in <see cref="SystemProviders"/>; custom providers
/// are persisted in the database under the <c>"custom-"</c> name prefix.
/// </summary>
/// <param name="Name">Unique kebab-case identifier (e.g. <c>"openrouter"</c>, <c>"custom-myllm"</c>).</param>
/// <param name="DisplayName">Human-readable name surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary shown in the provider picker.</param>
/// <param name="Type">Whether this provider is accessed via HTTP or a CLI binary.</param>
/// <param name="Settings">
/// Type-specific configuration. Parsed via <see cref="HttpProviderSettings.FromSettings"/> or
/// <see cref="CliProviderSettings.FromSettings"/> depending on <see cref="Type"/>.
/// </param>
/// <param name="IsSystem">True for code-constant providers defined in <see cref="SystemProviders"/>.</param>
/// <param name="IsActive">Whether this provider is currently available for use in crew profiles.</param>
/// <param name="CreatedAt">UTC timestamp of first persistence.</param>
/// <param name="UpdatedAt">UTC timestamp of last update.</param>
public sealed record Provider(
    string Name,
    string DisplayName,
    string Description,
    ProviderType Type,
    Dictionary<string, JsonElement> Settings,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
