using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Auth;

internal sealed class StaticTokenValidator(
    IOptions<AtelierMcpOptions> options,
    ILogger<StaticTokenValidator> logger) : ITokenValidator
{
    public Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;

        if (string.IsNullOrEmpty(opts.Token))
        {
            logger.LogWarning("MCP token validation rejected: AtelierMcp.Token is not configured");
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("MCP token validation rejected");
            return Task.FromResult(false);
        }

        var expectedBytes = Encoding.UTF8.GetBytes(opts.Token);
        var actualBytes   = Encoding.UTF8.GetBytes(token);

        if (expectedBytes.Length != actualBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            logger.LogWarning("MCP token validation rejected");
            return Task.FromResult(false);
        }

        logger.LogInformation("MCP token validation accepted");
        return Task.FromResult(true);
    }
}
