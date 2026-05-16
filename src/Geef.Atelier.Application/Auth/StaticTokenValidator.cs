using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Auth;

internal sealed class StaticTokenValidator(
    IOptions<AtelierMcpOptions> options,
    IOptions<AtelierUserOptions> userOptions,
    ILogger<StaticTokenValidator> logger) : ITokenValidator
{
    public Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;

        if (string.IsNullOrEmpty(opts.Token))
        {
            logger.LogWarning("MCP token validation rejected: AtelierMcp.Token is not configured");
            return Task.FromResult(TokenValidationOutcome.Invalid);
        }

        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("MCP token validation rejected");
            return Task.FromResult(TokenValidationOutcome.Invalid);
        }

        var expectedBytes = Encoding.UTF8.GetBytes(opts.Token);
        var actualBytes   = Encoding.UTF8.GetBytes(token);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            logger.LogWarning("MCP token validation rejected");
            return Task.FromResult(TokenValidationOutcome.Invalid);
        }

        var username = !string.IsNullOrEmpty(opts.StaticTokenUser)
            ? opts.StaticTokenUser
            : userOptions.Value.Username;

        logger.LogInformation("MCP token validation accepted for user {Username}", username);
        return Task.FromResult(new TokenValidationOutcome(true, "static-bearer", username, null, null));
    }
}
