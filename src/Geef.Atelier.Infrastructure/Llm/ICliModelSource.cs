namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>Model list of a CLI provider as reported by the cli-proxy sidecar.</summary>
/// <param name="ModelIds">Model ids in the order the proxy reported them.</param>
/// <param name="IsLive">True when the proxy resolved the list from the CLI itself.</param>
/// <param name="HasLiveSource">
/// False when this CLI kind has no live source at all, so its static list is authoritative
/// rather than degraded.
/// </param>
/// <param name="AliasMap">
/// Maps always-latest alias ids to the concrete model they currently resolve to. Empty when the
/// backend reported none.
/// </param>
internal sealed record CliModelCatalog(
    IReadOnlyList<string> ModelIds,
    bool IsLive,
    bool HasLiveSource,
    IReadOnlyDictionary<string, string> AliasMap);

/// <summary>Reads the model catalog of a CLI provider from the cli-proxy sidecar.</summary>
internal interface ICliModelSource
{
    /// <summary>
    /// Returns the catalog the proxy reports for <paramref name="providerName"/>, preserving the
    /// proxy's own ordering. Returns null when the request fails or times out, or when the response
    /// carries no usable ids — an empty result counts as a failure so the caller never renders an
    /// empty picker. Never throws for transport, status or parse errors; only a caller-triggered
    /// cancellation propagates.
    /// </summary>
    /// <param name="providerName">Provider name as configured in the Atelier, e.g. "claude-cli".</param>
    /// <param name="timeout">Budget for this single attempt.</param>
    /// <param name="bypassProxyCache">Asks the proxy to re-resolve instead of serving its own cache.</param>
    /// <param name="ct">Caller cancellation.</param>
    Task<CliModelCatalog?> TryGetCatalogAsync(
        string providerName, TimeSpan timeout, bool bypassProxyCache, CancellationToken ct = default);
}
