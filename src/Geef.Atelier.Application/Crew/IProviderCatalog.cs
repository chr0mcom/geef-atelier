namespace Geef.Atelier.Application.Crew;

/// <summary>A named LLM provider with a human-readable display name for the UI.</summary>
public sealed record ProviderInfo(string Name, string DisplayName);

/// <summary>Read-only catalogue of available LLM providers.</summary>
public interface IProviderCatalog
{
    /// <summary>Returns all available LLM providers with their display names.</summary>
    IReadOnlyList<ProviderInfo> ListProviders();
}
