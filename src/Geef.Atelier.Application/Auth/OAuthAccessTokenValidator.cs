using Geef.Atelier.Application.OAuth;

namespace Geef.Atelier.Application.Auth;

internal sealed class OAuthAccessTokenValidator(IOAuthService oAuthService) : ITokenValidator
{
    public async Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var result = await oAuthService.ValidateAccessTokenAsync(token, ct);
        if (!result.IsValid)
            return TokenValidationOutcome.Invalid;
        return new TokenValidationOutcome(true, "oauth-bearer", result.UserId, result.ClientId, result.Scope);
    }
}
