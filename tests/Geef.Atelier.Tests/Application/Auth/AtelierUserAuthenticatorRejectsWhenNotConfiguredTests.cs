using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorRejectsWhenNotConfiguredTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_NotConfigured_ReturnsFalse()
    {
        var opts = Options.Create(new AtelierUserOptions
        {
            Username     = "",
            PasswordHash = "",
        });
        var sut = new AtelierUserAuthenticator(opts, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("any", "any");

        Assert.False(result);
    }
}
