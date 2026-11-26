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

    public Task<UserProfileFillResult> FillEmptyAsync(string username, UserProfileFill fill, DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_profiles)
        {
            var old = _profiles.GetValueOrDefault(username) ?? new UserProfileEntity { Username = username, CreatedAt = now, UpdatedAt = now };
            var filled = new List<ProfileFillField>();
            var alreadySet = new List<ProfileFillField>();
            var formality = fill.Formality is { } f && f != Formality.Unset ? f : (Formality?)null;
            void Sort(ProfileFillField field, bool asked, bool empty)
            {
                if (asked)
                    (empty ? filled : alreadySet).Add(field);
            }
            Sort(ProfileFillField.DisplayName, fill.DisplayName is not null, old.DisplayName is null);
            Sort(ProfileFillField.Formality, formality is not null, old.Formality == Formality.Unset);
            Sort(ProfileFillField.Language, fill.Language is not null, old.Language is null);
            Sort(ProfileFillField.Signature, fill.Signature is not null, old.Signature is null);
            _profiles[username] = old with
            {
                DisplayName = old.DisplayName ?? fill.DisplayName,
                Formality = old.Formality == Formality.Unset && formality is { } chosen ? chosen : old.Formality,
                Language = old.Language ?? fill.Language,
                Signature = old.Signature ?? fill.Signature,
                UpdatedAt = filled.Count > 0 ? now : old.UpdatedAt,
            };
            return Task.FromResult(new UserProfileFillResult(filled, alreadySet));
        }
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
