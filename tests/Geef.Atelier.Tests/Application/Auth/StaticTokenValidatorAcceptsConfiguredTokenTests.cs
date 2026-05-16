using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class StaticTokenValidatorAcceptsConfiguredTokenTests
{
    [Fact]
    public async Task ValidateTokenAsync_CorrectToken_ReturnsTrue()
    {
        var opts = Options.Create(new AtelierMcpOptions { Token = "valid-token" });
        var sut = new StaticTokenValidator(opts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("valid-token");

        Assert.True(result.IsValid);
    }
}
