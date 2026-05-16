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
        var opts = Options.Create(new AtelierMcpOptions { Token = "" });
        var sut = new StaticTokenValidator(opts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("any-token");

        Assert.False(result.IsValid);
    }
}
