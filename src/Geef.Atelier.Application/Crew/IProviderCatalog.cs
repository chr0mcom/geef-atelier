namespace Geef.Atelier.Application.Crew;

/// <summary>Read-only catalogue of configured LLM provider names.</summary>
public interface IProviderCatalog
{
    /// <summary>Returns the names of all configured LLM providers, sorted alphabetically.</summary>
    IReadOnlyList<string> ListProviderNames();
}
