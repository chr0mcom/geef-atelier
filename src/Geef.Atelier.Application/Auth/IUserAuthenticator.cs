using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Auth;

/// <summary>Validates user credentials against the user store.</summary>
public interface IUserAuthenticator
{
    /// <summary>
    /// Returns the authenticated <see cref="AtelierUser"/> if credentials are valid and the account is active;
    /// otherwise <see langword="null"/>.
    /// </summary>
    Task<AtelierUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);
}
