using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Application.Auth;

/// <summary>
/// Manages accounts. The "About me" profile hangs on the user name, so it goes with the account: it is deleted with the
/// account, moved on a rename, and a new account never finds an old profile under its name.
/// </summary>
internal sealed class UserAdminService(
    IAtelierUserRepository users,
    IOAuthService oauthService,
    IUserProfileRepository profiles) : IUserAdminService
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

        await profiles.DeleteAsync(username, ct);
        await users.AddAsync(user, ct);
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

        await users.UpdateAsync(updated, ct);
        if (!string.Equals(existing.Username, username, StringComparison.Ordinal))
            await MoveProfileAsync(existing.Username, username, ct);

        if (!isActive && existing.IsActive)
            await oauthService.RevokeAllUserTokensAsync(existing.Username, ct);

        return updated;
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        var existing = await users.FindByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

        await oauthService.RevokeAllUserTokensAsync(existing.Username, ct);
        await users.DeleteAsync(userId, ct);
        await profiles.DeleteAsync(existing.Username, ct);
    }

    private async Task MoveProfileAsync(string from, string to, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(from, ct);
        await profiles.DeleteAsync(to, ct);
        if (profile is null)
            return;
        await profiles.UpsertAsync(profile with { Username = to }, ct);
        await profiles.DeleteAsync(from, ct);
    }
}
