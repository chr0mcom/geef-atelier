using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorRejectsInvalidPasswordTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_WrongPassword_ReturnsFalse()
    {
        var opts = Options.Create(new AtelierUserOptions
        {
            Username     = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password", workFactor: 4),
        });
        var sut = new AtelierUserAuthenticator(opts, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("testuser", "wrong-password");

        Assert.False(result);
    }
}
