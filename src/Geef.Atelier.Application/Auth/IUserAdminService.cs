using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Auth;

/// <summary>Admin operations for managing Atelier user accounts.</summary>
public interface IUserAdminService
{
    /// <summary>Returns all user accounts (admin-only listing).</summary>
    Task<IReadOnlyList<AtelierUser>> GetAllUsersAsync(CancellationToken ct);

    /// <summary>Creates a new account; the password is BCrypt-hashed before storage.</summary>
    Task<AtelierUser> CreateUserAsync(string username, string password, string? email, CancellationToken ct);

    /// <summary>Updates an account. <paramref name="newPassword"/> null leaves the password unchanged; setting <paramref name="isActive"/> to false also revokes the user's OAuth tokens.</summary>
    Task<AtelierUser> UpdateUserAsync(string userId, string username, string? newPassword, string? email, bool isActive, CancellationToken ct);

    /// <summary>Deletes an account and revokes its OAuth tokens. Runs created by the user are intentionally retained (D-042/6).</summary>
    Task DeleteUserAsync(string userId, CancellationToken ct);
}
