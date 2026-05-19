namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>Configuration options for the CLI proxy sidecar.</summary>
public sealed class CliProxyOptions
{
    /// <summary>Base URL of the CLI proxy service.</summary>
    public string BaseUrl { get; set; } = "http://cli-proxy:8090";

    /// <summary>Shared secret used to authenticate internal API calls to <c>/api/internal/*</c>.</summary>
    public string InternalApiToken { get; set; } = "";
}
