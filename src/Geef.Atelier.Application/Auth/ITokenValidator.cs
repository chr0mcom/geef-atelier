namespace Geef.Atelier.Application.Auth;

public interface ITokenValidator
{
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
