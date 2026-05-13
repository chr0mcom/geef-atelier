using Geef.Atelier.Application.Crew.Grounding;

namespace Geef.Atelier.Infrastructure.Grounding;

internal sealed class GroundingProviderFactory(IEnumerable<IGroundingProvider> providers) : IGroundingProviderFactory
{
    private readonly IReadOnlyDictionary<string, IGroundingProvider> _providers =
        providers.ToDictionary(p => p.ProviderType, StringComparer.OrdinalIgnoreCase);

    public IGroundingProvider Create(string providerType)
    {
        if (_providers.TryGetValue(providerType, out var provider))
            return provider;
        throw new InvalidOperationException(
            $"No grounding provider is registered for type '{providerType}'. Registered types: {string.Join(", ", _providers.Keys)}");
    }

    public bool IsRegistered(string providerType) =>
        _providers.ContainsKey(providerType);
}
