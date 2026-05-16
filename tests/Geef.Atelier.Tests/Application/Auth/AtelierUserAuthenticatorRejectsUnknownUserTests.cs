using Geef.Atelier.Application.Auth;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorRejectsUnknownUserTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_UnknownUsername_ReturnsNull()
    {
        var repo = new InMemoryAtelierUserRepository();
        repo.Seed("testuser", "correct-password");
        var sut = new AtelierUserAuthenticator(repo, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("otheruser", "correct-password");

        Assert.Null(result);
    }
}
