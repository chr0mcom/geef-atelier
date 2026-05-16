namespace Geef.Atelier.Application.Auth;

internal sealed class CompositeTokenValidator(
    StaticTokenValidator staticValidator,
    OAuthAccessTokenValidator oauthValidator) : ITokenValidator
{
    public async Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var staticResult = await staticValidator.ValidateTokenAsync(token, ct);
        if (staticResult.IsValid) return staticResult;
        return await oauthValidator.ValidateTokenAsync(token, ct);
    }
}
