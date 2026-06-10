using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Mcp;

public sealed class McpToolDiscoveryServiceTests
{
    // -----------------------------------------------------------------------
    // SanitizeName — underscore → kebab-case
    // -----------------------------------------------------------------------

    [Fact]
    public void SanitizeName_UnderscoreName_BecomesKebabCase()
    {
        var result = McpToolDiscoveryService.SanitizeName("search_the_web", Guid.NewGuid());

        Assert.Equal("search-the-web", result);
    }

    // -----------------------------------------------------------------------
    // SanitizeName — uppercase → lowercase
    // -----------------------------------------------------------------------

    [Fact]
    public void SanitizeName_UpperCaseName_Lowercased()
    {
        var result = McpToolDiscoveryService.SanitizeName("FetchDocument", Guid.NewGuid());

        Assert.Equal("fetchdocument", result);
    }

    // -----------------------------------------------------------------------
    // SanitizeName — special chars stripped
    // -----------------------------------------------------------------------

    [Fact]
    public void SanitizeName_SpecialChars_Stripped()
    {
        var result = McpToolDiscoveryService.SanitizeName("get!@#$%document", Guid.NewGuid());

        // Only [a-z0-9-] pass through; '!' '@' '#' '$' '%' are removed
        Assert.Equal("getdocument", result);
    }

    // -----------------------------------------------------------------------
    // DiscoverAsync — factory throws → exception propagates
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DiscoverAsync_SsrfBlocked_PropagatesException()
    {
        var factory = new ThrowingMcpClientFactory();
        var service = new McpToolDiscoveryService(factory, NullLogger<McpToolDiscoveryService>.Instance);
        var config  = new McpServerConfig
        {
            Id       = Guid.NewGuid(),
            Name     = "Blocked",
            Url      = "http://192.168.1.1/mcp",
            IsActive = true,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DiscoverAsync(config));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class ThrowingMcpClientFactory : IAtelierMcpClientFactory
    {
        public Task<ModelContextProtocol.Client.McpClient> ConnectAsync(
            McpServerConfig config, CancellationToken ct = default) =>
            throw new InvalidOperationException("SSRF blocked");
    }
}
