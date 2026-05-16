using Geef.Atelier.Application.Auth;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorRejectsWhenNotConfiguredTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_EmptyUserStore_ReturnsNull()
    {
        var repo = new InMemoryAtelierUserRepository();
        var sut  = new AtelierUserAuthenticator(repo, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("any", "any");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_InactiveAccount_ReturnsNull()
    {
        var repo = new InMemoryAtelierUserRepository();
        repo.Seed("testuser", "correct-password", isActive: false);
        var sut  = new AtelierUserAuthenticator(repo, NullLogger<AtelierUserAuthenticator>.Instance);

        var result = await sut.ValidateCredentialsAsync("testuser", "correct-password");

        Assert.Null(result);
    }
}
