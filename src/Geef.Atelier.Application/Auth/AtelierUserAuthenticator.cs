using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Auth;

internal sealed class AtelierUserAuthenticator(
    IOptions<AtelierUserOptions> options,
    ILogger<AtelierUserAuthenticator> logger) : IUserAuthenticator
{
    public Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;

        if (string.IsNullOrEmpty(opts.Username) || string.IsNullOrEmpty(opts.PasswordHash))
        {
            logger.LogWarning("Login attempt rejected: AtelierUser is not configured");
            return Task.FromResult(false);
        }

        var expectedBytes = Encoding.UTF8.GetBytes(opts.Username);
        var actualBytes   = Encoding.UTF8.GetBytes(username);
        var usernameMatch = CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);

        var passwordValid = BCrypt.Net.BCrypt.Verify(password, opts.PasswordHash);

        if (!usernameMatch)
        {
            logger.LogWarning("Login attempt rejected");
            return Task.FromResult(false);
        }

        if (!passwordValid)
            logger.LogWarning("Login attempt rejected");

        return Task.FromResult(passwordValid);
    }
}
