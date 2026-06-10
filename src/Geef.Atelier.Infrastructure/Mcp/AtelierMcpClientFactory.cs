using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Geef.Atelier.Infrastructure.Mcp;

internal sealed class AtelierMcpClientFactory(
    IUrlSafetyValidator urlSafetyValidator,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory) : IAtelierMcpClientFactory
{
    public async Task<McpClient> ConnectAsync(McpServerConfig config, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"MCP server URL '{config.Url}' is not a valid absolute URI.");

        var safety = await urlSafetyValidator.ValidateAsync(uri, ct);
        if (!safety.IsAllowed)
            throw new InvalidOperationException(
                $"MCP server URL '{config.Url}' was blocked by SSRF policy: {safety.RejectionReason}");

        var httpClient = httpClientFactory.CreateClient("mcp-client");
        if (!string.IsNullOrWhiteSpace(config.AuthHeaderEnv))
        {
            var token = Environment.GetEnvironmentVariable(config.AuthHeaderEnv);
            if (!string.IsNullOrWhiteSpace(token))
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = uri },
            httpClient,
            loggerFactory,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, clientOptions: null, loggerFactory: loggerFactory, ct);
    }
}
