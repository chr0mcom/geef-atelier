using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Fakes;

public sealed class InMemoryAtelierUserRepository : IAtelierUserRepository
{
    private readonly Dictionary<string, AtelierUser> _byId       = new();
    private readonly Dictionary<string, AtelierUser> _byUsername = new(StringComparer.Ordinal);

    public Task<AtelierUser?> FindByUsernameAsync(string username, CancellationToken ct)
        => Task.FromResult(_byUsername.GetValueOrDefault(username));

    public Task<AtelierUser?> FindByUserIdAsync(string userId, CancellationToken ct)
        => Task.FromResult(_byId.GetValueOrDefault(userId));

    public Task<IReadOnlyList<AtelierUser>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AtelierUser>>(_byId.Values.OrderBy(u => u.Username).ToList());

    public Task AddAsync(AtelierUser user, CancellationToken ct)
    {
        _byId[user.UserId]         = user;
        _byUsername[user.Username] = user;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AtelierUser user, CancellationToken ct)
    {
        if (_byId.TryGetValue(user.UserId, out var old))
            _byUsername.Remove(old.Username);
        _byId[user.UserId]         = user;
        _byUsername[user.Username] = user;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string userId, CancellationToken ct)
    {
        if (_byId.TryGetValue(userId, out var user))
        {
            _byUsername.Remove(user.Username);
            _byId.Remove(userId);
        }
        return Task.CompletedTask;
    }

    public AtelierUser Seed(string username, string passwordPlainText, bool isActive = true, bool isAdmin = false)
    {
        var user = new AtelierUser(
            UserId:       Guid.NewGuid().ToString(),
            Username:     username,
            PasswordHash: BCrypt.Net.BCrypt.HashPassword(passwordPlainText, workFactor: 4),
            Email:        null,
            IsActive:     isActive,
            IsAdmin:      isAdmin,
            CreatedAt:    DateTimeOffset.UtcNow,
            UpdatedAt:    DateTimeOffset.UtcNow);
        _byId[user.UserId]         = user;
        _byUsername[user.Username] = user;
        return user;
    }
}
