using Geef.Atelier.Application.Auth;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorValidatesCorrectCredentialsTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_CorrectCredentials_ReturnsUser()
    {
        var repo = new InMemoryAtelierUserRepository();
        repo.Seed("testuser", "correct-password");
        var sut = new AtelierUserAuthenticator(repo, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("testuser", "correct-password");

        Assert.NotNull(result);
        Assert.Equal("testuser", result.Username);
    }
}
