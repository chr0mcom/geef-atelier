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

    public Task<UserProfileView> SavePartAsync(string username, UserProfilePart part, CancellationToken ct = default)
    {
        SavedParts.Enqueue((username, part));
        if (SaveError is not null)
            throw SaveError;
        var old = Stored(username) ?? Empty;
        var merged = old with
        {
            DisplayName = Trim(part.DisplayName) ?? old.DisplayName,
            Formality = part.Formality ?? old.Formality,
            Language = Trim(part.Language) ?? old.Language,
            Signature = Trim(part.Signature) ?? old.Signature,
        };
        _profiles[username] = merged;
        return Task.FromResult(View(merged));
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
