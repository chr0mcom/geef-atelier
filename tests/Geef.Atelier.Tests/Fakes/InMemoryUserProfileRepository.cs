using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Tests.Fakes;

public sealed class InMemoryUserProfileRepository : IUserProfileRepository
{
    private readonly ConcurrentDictionary<string, UserProfileEntity> _profiles = new(StringComparer.Ordinal);

    public IReadOnlyCollection<UserProfileEntity> All => _profiles.Values.ToList();

    public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(_profiles.GetValueOrDefault(username));

    public Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default)
    {
        _profiles[profile.Username] = profile;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(_profiles.TryRemove(username, out _));

    public static UserProfileEntity Profile(string username, string freeText) => new()
    {
        Username = username,
        DisplayName = "Alex Beispiel",
        FreeText = freeText,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
