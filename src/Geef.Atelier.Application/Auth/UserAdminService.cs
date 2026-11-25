using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Application.Auth;

/// <summary>
/// Manages accounts. The "About me" profile hangs on the user name, so it goes with the account (see <see cref="IAccountStore"/>).
/// The tokens of an account are revoked before anything else changes, so a failure later never leaves a deactivated, renamed or
/// deleted account with working tokens.
/// </summary>
internal sealed class UserAdminService(
    IAtelierUserRepository users,
    IOAuthService oauthService,
    IAccountStore accounts) : IUserAdminService
{
    public Task<IReadOnlyList<AtelierUser>> GetAllUsersAsync(CancellationToken ct)
        => users.GetAllAsync(ct);

    public async Task<AtelierUser> CreateUserAsync(string username, string password, string? email, CancellationToken ct)
    {
        var existing = await users.FindByUsernameAsync(username, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Username '{username}' is already taken.");

        var user = new AtelierUser(
            UserId: Guid.NewGuid().ToString(),
            Username: username,
            PasswordHash: BCrypt.Net.BCrypt.HashPassword(password),
            Email: email,
            IsActive: true,
            IsAdmin: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await accounts.AddAsync(user, ct);
        return user;
    }

    public async Task<AtelierUser> UpdateUserAsync(
        string userId, string username, string? newPassword, string? email, bool isActive, CancellationToken ct)
    {
        var existing = await users.FindByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        var passwordHash = newPassword is not null
            ? BCrypt.Net.BCrypt.HashPassword(newPassword)
            : existing.PasswordHash;

        var updated = existing with
        {
            Username = username,
            PasswordHash = passwordHash,
            Email = email,
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var renamed = !string.Equals(existing.Username, username, StringComparison.Ordinal);
        if (renamed || (!isActive && existing.IsActive))
            await oauthService.RevokeAllUserTokensAsync(existing.Username, ct);

        await accounts.UpdateAsync(updated, existing.Username, ct);
        return updated;
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        var existing = await users.FindByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await oauthService.RevokeAllUserTokensAsync(existing.Username, ct);
        await accounts.DeleteAsync(existing, ct);
    }
}
