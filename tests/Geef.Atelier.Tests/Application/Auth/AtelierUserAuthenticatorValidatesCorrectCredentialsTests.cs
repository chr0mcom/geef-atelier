using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorValidatesCorrectCredentialsTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_CorrectCredentials_ReturnsTrue()
    {
        var opts = Options.Create(new AtelierUserOptions
        {
            Username     = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password", workFactor: 4),
        });
        var sut = new AtelierUserAuthenticator(opts, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("testuser", "correct-password");

        Assert.True(result);
    }
}
