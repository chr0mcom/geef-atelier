using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class StaticTokenValidatorRejectsWhenNotConfiguredTests
{
    [Fact]
    public async Task ValidateTokenAsync_EmptyConfig_ReturnsFalse()
    {
        var opts     = Options.Create(new AtelierMcpOptions { Token = "" });
        var userOpts = Options.Create(new AtelierUserOptions { Username = "admin" });
        var sut = new StaticTokenValidator(opts, userOpts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("any-token");

        Assert.False(result.IsValid);
    }
}
