namespace Geef.Atelier.Application.Auth;

public interface ITokenValidator
{
    Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
