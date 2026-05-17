namespace Geef.Atelier.Application.Auth;

/// <summary>
/// Validates a Bearer token presented on the MCP endpoint. Implementations resolve
/// the token against one or more sources (static <c>ATELIER_MCP_TOKEN</c>, OAuth
/// access token) and return a <see cref="TokenValidationOutcome"/> describing the
/// authenticated principal — never throwing for an invalid token.
/// </summary>
public interface ITokenValidator
{
    /// <summary>
    /// Validates <paramref name="token"/> and returns the outcome. The result is
    /// <see cref="TokenValidationOutcome.Invalid"/> (not an exception) when the token
    /// is unknown, expired or revoked.
    /// </summary>
    Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
