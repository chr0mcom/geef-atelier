using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Mcp;

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------

internal sealed class AllowAllUrlSafetyValidator : IUrlSafetyValidator
{
    public Uri? LastValidated { get; private set; }

    public Task<UrlSafetyResult> ValidateAsync(Uri url, CancellationToken ct = default)
    {
        LastValidated = url;
        return Task.FromResult(new UrlSafetyResult(IsAllowed: true, RejectionReason: null));
    }
}

internal sealed class BlockAllUrlSafetyValidator : IUrlSafetyValidator
{
    public Task<UrlSafetyResult> ValidateAsync(Uri url, CancellationToken ct = default) =>
        Task.FromResult(new UrlSafetyResult(IsAllowed: false, RejectionReason: "SSRF policy: private address"));
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class AtelierMcpClientFactoryTests
{
    private static AtelierMcpClientFactory BuildFactory(IUrlSafetyValidator validator) =>
        new(validator, new StubHttpClientFactory(), NullLoggerFactory.Instance);

    private static McpServerConfig MakeConfig(string url, string? authHeaderEnv = null) =>
        new()
        {
            Id            = Guid.NewGuid(),
            Name          = "Test Server",
            Url           = url,
            AuthHeaderEnv = authHeaderEnv,
            IsActive      = true,
            UpdatedAt     = DateTimeOffset.UtcNow,
        };

    // -----------------------------------------------------------------------
    // AC 1: Invalid URL throws before any network activity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_InvalidUrl_Throws()
    {
        var factory = BuildFactory(new AllowAllUrlSafetyValidator());
        var config  = MakeConfig("not-a-url");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.ConnectAsync(config));

        Assert.Contains("not a valid absolute URI", ex.Message);
    }

    // -----------------------------------------------------------------------
    // AC 2: SSRF-blocked URL throws before connecting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_SsrfBlocked_Throws()
    {
        var factory = BuildFactory(new BlockAllUrlSafetyValidator());
        var config  = MakeConfig("http://192.168.1.1/mcp");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.ConnectAsync(config));

        Assert.Contains("SSRF policy", ex.Message);
    }

    // -----------------------------------------------------------------------
    // AC 3: SSRF-allowed URL — validator receives the correct URI
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_SsrfAllowed_PassesCorrectUriToValidator()
    {
        const string url       = "https://tools.example.com/mcp";
        var validator          = new AllowAllUrlSafetyValidator();
        var factory            = BuildFactory(validator);
        var config             = MakeConfig(url);

        // The factory will pass SSRF validation but then attempt to connect
        // to a non-existent server. Catch whatever the transport throws.
        try
        {
            await factory.ConnectAsync(config);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Any exception other than InvalidOperationException is expected
            // because there is no real MCP server listening.
        }

        Assert.NotNull(validator.LastValidated);
        Assert.Equal(new Uri(url), validator.LastValidated);
    }
}
