namespace Geef.Atelier.Application.Auth;

/// <summary>Validates user credentials against the configured single-user account.</summary>
public interface IUserAuthenticator
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="username"/> and <paramref name="password"/>
    /// match the configured credentials; otherwise <see langword="false"/>.
    /// </summary>
    Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);
}
