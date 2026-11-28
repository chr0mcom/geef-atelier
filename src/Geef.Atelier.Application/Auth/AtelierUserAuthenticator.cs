using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Application.Auth;

internal sealed class AtelierUserAuthenticator(
    IAtelierUserRepository users,
    ILogger<AtelierUserAuthenticator> logger) : IUserAuthenticator
{
    private static readonly Lazy<string> DecoyHash = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")));

    public async Task<AtelierUser?> ValidateCredentialsAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByUsernameAsync(username, cancellationToken);
        var passwordMatches = BCrypt.Net.BCrypt.Verify(password, user is { IsActive: true } ? user.PasswordHash : DecoyHash.Value);
        if (user is null)
        {
            logger.LogWarning("Login attempt rejected: unknown username");
            return null;
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login attempt rejected: account inactive");
            return null;
        }

        if (!passwordMatches)
        {
            logger.LogWarning("Login attempt rejected: wrong password");
            return null;
        }

        return user;
    }
}
