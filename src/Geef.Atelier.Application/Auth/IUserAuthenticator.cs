using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Auth;

/// <summary>Validates user credentials against the user store.</summary>
public interface IUserAuthenticator
{
    /// <summary>
    /// Returns the authenticated <see cref="AtelierUser"/> if credentials are valid and the account is active;
    /// otherwise <see langword="null"/>. A name without an active account costs a password check
    /// as well, so the time of a rejection does not tell whether the name exists.
    /// </summary>
    Task<AtelierUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);
}
