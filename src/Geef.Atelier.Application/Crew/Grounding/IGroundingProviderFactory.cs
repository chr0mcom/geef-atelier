namespace Geef.Atelier.Application.Crew.Grounding;

/// <summary>
/// Resolves an <see cref="IGroundingProvider"/> by its <c>ProviderType</c> discriminator.
/// Implementations are registered in DI at startup; unknown types throw at resolution time.
/// </summary>
public interface IGroundingProviderFactory
{
    /// <summary>
    /// Returns the registered provider for the given <paramref name="providerType"/>.
    /// Throws <see cref="InvalidOperationException"/> when no provider is registered for that type.
    /// </summary>
    IGroundingProvider Create(string providerType);

    /// <summary>Returns true when a provider is registered for the given <paramref name="providerType"/>.</summary>
    bool IsRegistered(string providerType);

    /// <summary>All registered provider-type discriminators (e.g. <c>tavily</c>, <c>academic-search</c>).</summary>
    IReadOnlyCollection<string> RegisteredTypes { get; }
}
