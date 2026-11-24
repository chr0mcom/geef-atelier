using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Application.Profiles;

internal sealed partial class ProfileService(IUserProfileRepository profiles, TimeProvider time) : IProfileService
{
    internal const string LanguageMessage = "Please enter the language as a two-letter code, for example de or en.";

    public async Task<UserProfileView> GetAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return ToView(await profiles.GetAsync(username, ct));
    }

    public async Task<UserProfileView> SaveAsync(string username, UserProfileInput input, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(input);

        var language = Clean(input.Language, "Language", UserProfileLimits.Language)?.ToLowerInvariant();
        if (language is not null && !LanguageCode().IsMatch(language))
            throw new ProfileValidationException(LanguageMessage);
        if (!Enum.IsDefined(input.Formality))
            throw new ProfileValidationException("Please choose how readers are addressed.");

        var now = time.GetUtcNow();
        var existing = await profiles.GetAsync(username, ct);
        var profile = new UserProfileEntity
        {
            Username = username,
            DisplayName = Clean(input.DisplayName, "Your name", UserProfileLimits.DisplayName),
            Role = Clean(input.Role, "What you do", UserProfileLimits.Role),
            Organization = Clean(input.Organization, "Organization", UserProfileLimits.Organization),
            Formality = input.Formality,
            Language = language,
            Signature = Clean(input.Signature, "Signature", UserProfileLimits.Signature),
            NoGos = Clean(input.NoGos, "Never do this", UserProfileLimits.NoGos),
            FreeText = Clean(input.FreeText, "Anything else we should know", UserProfileLimits.FreeText),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
        await profiles.UpsertAsync(profile, ct);
        return ToView(profile);
    }

    public async Task ClearAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await profiles.DeleteAsync(username, ct);
    }

    private static UserProfileView ToView(UserProfileEntity? profile) => profile is null
        ? new UserProfileView(new UserProfileInput(null, null, null, Formality.Unset, null, null, null, null), true, null)
        : new UserProfileView(
            new UserProfileInput(profile.DisplayName, profile.Role, profile.Organization, profile.Formality, profile.Language, profile.Signature, profile.NoGos, profile.FreeText),
            ProfilePrompt.IsEmpty(profile),
            profile.UpdatedAt);

    private static string? Clean(string? value, string label, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > limit)
            throw new ProfileValidationException(string.Create(CultureInfo.InvariantCulture,
                $"\"{label}\" is too long ({trimmed.Length:N0} characters, at most {limit:N0})."));
        return trimmed;
    }

    [GeneratedRegex("^[a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();
}
