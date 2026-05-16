using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

/// <summary>
/// Tests that StaticTokenValidator resolves the username from
/// AtelierMcpOptions.StaticTokenUser when configured, and falls back to
/// AtelierUserOptions.Username when it is not.
/// </summary>
public sealed class StaticTokenValidatorUserMappingTests
{
    [Fact]
    public async Task ValidateToken_WithStaticTokenUserConfigured_ReturnsConfiguredUsername()
    {
        var mcpOpts  = Options.Create(new AtelierMcpOptions { Token = "my-token", StaticTokenUser = "configured-user" });
        var userOpts = Options.Create(new AtelierUserOptions  { Username = "fallback-user" });
        var sut      = new StaticTokenValidator(mcpOpts, userOpts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("my-token");

        Assert.True(result.IsValid);
        Assert.Equal("configured-user", result.Subject);
    }

    [Fact]
    public async Task ValidateToken_WithoutStaticTokenUser_ReturnsAtelierUserUsername()
    {
        // StaticTokenUser is not set → validator must fall back to AtelierUserOptions.Username
        var mcpOpts  = Options.Create(new AtelierMcpOptions { Token = "my-token", StaticTokenUser = null });
        var userOpts = Options.Create(new AtelierUserOptions  { Username = "fallback-user" });
        var sut      = new StaticTokenValidator(mcpOpts, userOpts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("my-token");

        Assert.True(result.IsValid);
        Assert.Equal("fallback-user", result.Subject);
    }
}
