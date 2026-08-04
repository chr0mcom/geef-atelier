using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Model catalog for tests that merely need the dependency satisfied: empty lists, unknown
/// provenance, identity model resolution.
/// </summary>
/// <remarks>
/// Identity resolution is the null effect of alias resolution, so this double cannot be used to
/// assert anything about it — use <see cref="ResolvingModelCatalog"/> where the resolution itself
/// is the subject.
/// </remarks>
internal sealed class PassThroughModelCatalog : IModelCatalog
{
    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public ModelCatalogSource GetSource(string providerName) => ModelCatalogSource.Unknown;

    public bool IsUsingFallback(string providerName) => false;

    public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
        => Task.FromResult(modelId);
}

/// <summary>
/// Model catalog with a configurable alias map and model list, for tests whose subject is alias
/// resolution or the behaviour that depends on it.
/// </summary>
internal sealed class ResolvingModelCatalog(
    IReadOnlyDictionary<string, string>? aliases = null,
    IReadOnlyList<ModelInfo>? models = null) : IModelCatalog
{
    private readonly IReadOnlyDictionary<string, string> _aliases =
        aliases ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<ModelInfo> _models = models ?? [];

    /// <summary>Every model id this catalog was asked to resolve, in call order.</summary>
    public List<string> ResolveCalls { get; } = [];

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult(_models);

    public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult(_models);

    public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default)
        => Task.FromResult(_models);

    public ModelCatalogSource GetSource(string providerName) => ModelCatalogSource.Live;

    public bool IsUsingFallback(string providerName) => false;

    public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
    {
        ResolveCalls.Add(modelId);
        return Task.FromResult(_aliases.TryGetValue(modelId, out var resolved) ? resolved : modelId);
    }
}
