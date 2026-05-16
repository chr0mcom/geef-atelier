using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Auth;

/// <summary>Admin operations for managing Atelier user accounts.</summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<AtelierUser>> GetAllUsersAsync(CancellationToken ct);
    Task<AtelierUser> CreateUserAsync(string username, string password, string? email, CancellationToken ct);
    Task<AtelierUser> UpdateUserAsync(string userId, string username, string? newPassword, string? email, bool isActive, CancellationToken ct);
    Task DeleteUserAsync(string userId, CancellationToken ct);
}
