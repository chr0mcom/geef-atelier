using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class StaticTokenValidatorRejectsWrongTokenTests
{
    [Fact]
    public async Task ValidateTokenAsync_WrongToken_ReturnsFalse()
    {
        var opts     = Options.Create(new AtelierMcpOptions { Token = "valid-token" });
        var userOpts = Options.Create(new AtelierUserOptions { Username = "admin" });
        var sut = new StaticTokenValidator(opts, userOpts, NullLogger<StaticTokenValidator>.Instance);

        var result = await sut.ValidateTokenAsync("wrong-token");

        Assert.False(result.IsValid);
    }
}
