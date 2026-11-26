using System.Collections.Concurrent;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Tests.Web.Components.Profile;

/// <summary>In-memory <see cref="IProfileService"/> for page tests: profiles per user name, every call recorded, errors set by the test.</summary>
internal sealed class FakeProfileService : IProfileService
{
    public static readonly UserProfileInput Empty = new(null, null, null, Formality.Unset, null, null, null, null);

    private readonly ConcurrentDictionary<string, UserProfileInput> _profiles = new();

    public ConcurrentQueue<string> Loaded { get; } = new();
    public ConcurrentQueue<(string Username, UserProfileInput Input)> Saved { get; } = new();
    public ConcurrentQueue<string> Cleared { get; } = new();
    public Exception? GetError { get; set; }
    public Exception? SaveError { get; set; }

    public void Set(string username, UserProfileInput input) => _profiles[username] = input;

    public UserProfileInput? Stored(string username) => _profiles.TryGetValue(username, out var input) ? input : null;

    public Task<UserProfileView> GetAsync(string username, CancellationToken ct = default)
    {
        Loaded.Enqueue(username);
        if (GetError is not null)
            throw GetError;
        return Task.FromResult(View(_profiles.TryGetValue(username, out var input) ? input : null));
    }

    public Task<UserProfileView> SaveAsync(string username, UserProfileInput input, CancellationToken ct = default)
    {
        Saved.Enqueue((username, input));
        if (SaveError is not null)
            throw SaveError;
        var trimmed = new UserProfileInput(Trim(input.DisplayName), Trim(input.Role), Trim(input.Organization), input.Formality,
            Trim(input.Language), Trim(input.Signature), Trim(input.NoGos), Trim(input.FreeText));
        _profiles[username] = trimmed;
        return Task.FromResult(View(trimmed));
    }

    public ConcurrentQueue<(string Username, UserProfilePart Part)> SavedParts { get; } = new();

    public Task<UserProfilePartResult> SavePartAsync(string username, UserProfilePart part, CancellationToken ct = default)
    {
        SavedParts.Enqueue((username, part));
        if (SaveError is not null)
            throw SaveError;
        var old = Stored(username) ?? Empty;
        var saved = new List<ProfileFillField>();
        var already = new List<ProfileFillField>();
        void Sort(ProfileFillField field, bool asked, bool empty)
        {
            if (asked)
                (empty ? saved : already).Add(field);
        }
        Sort(ProfileFillField.DisplayName, part.DisplayName is not null, old.DisplayName is null);
        Sort(ProfileFillField.Formality, part.Formality is not null, old.Formality == Formality.Unset);
        Sort(ProfileFillField.Language, part.Language is not null, old.Language is null);
        Sort(ProfileFillField.Signature, part.Signature is not null, old.Signature is null);
        var merged = old with
        {
            DisplayName = old.DisplayName ?? Trim(part.DisplayName),
            Formality = old.Formality == Formality.Unset ? part.Formality ?? old.Formality : old.Formality,
            Language = old.Language ?? Trim(part.Language),
            Signature = old.Signature ?? Trim(part.Signature),
        };
        _profiles[username] = merged;
        return Task.FromResult(new UserProfilePartResult(View(merged), saved, already));
    }

    public Task ClearAsync(string username, CancellationToken ct = default)
    {
        Cleared.Enqueue(username);
        _profiles.TryRemove(username, out _);
        return Task.CompletedTask;
    }

    private static UserProfileView View(UserProfileInput? input) =>
        input is null || input == Empty
            ? new UserProfileView(Empty, true, null)
            : new UserProfileView(input, false, new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
