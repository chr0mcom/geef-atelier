using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Application.Profiles;

internal sealed partial class ProfileService(IUserProfileRepository profiles, TimeProvider time) : IProfileService
{
    internal const string LanguageMessage = "Please enter the language as a two-letter code, for example de or en.";
    internal const string NothingToSaveMessage = "There is nothing to save. Please fill in the field first.";
    internal const string FormalityMessage = "Please choose how readers are addressed.";
    internal const string SingleLineFormat = "\"{0}\" must be a single line without special characters.";
    internal const string SignatureCharactersMessage = "\"Signature\" can only contain line breaks, no other special characters.";
    internal const string SignatureLabelFormat = "A line of \"Signature\" cannot start with \"{0}:\", because that is the name of a field of About me.";

    public async Task<UserProfileView> GetAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return ToView(await profiles.GetAsync(username, ct));
    }

    public async Task<UserProfileView> SaveAsync(string username, UserProfileInput input, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(input);

        var language = CleanLanguage(input.Language);
        if (!Enum.IsDefined(input.Formality))
            throw new ProfileValidationException(FormalityMessage);

        var now = time.GetUtcNow();
        var existing = await profiles.GetAsync(username, ct);
        var profile = new UserProfileEntity
        {
            Username = username,
            DisplayName = CleanLine(input.DisplayName, "Your name", UserProfileLimits.DisplayName),
            Role = CleanLine(input.Role, "What you do", UserProfileLimits.Role),
            Organization = CleanLine(input.Organization, "Organization", UserProfileLimits.Organization),
            Formality = input.Formality,
            Language = language,
            Signature = CleanSignature(input.Signature),
            NoGos = Clean(input.NoGos, "Never do this", UserProfileLimits.NoGos),
            FreeText = Clean(input.FreeText, "Anything else we should know", UserProfileLimits.FreeText),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
        await profiles.UpsertAsync(profile, ct);
        return ToView(profile);
    }

    public async Task<UserProfilePartResult> SavePartAsync(string username, UserProfilePart part, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(part);

        if (part.DisplayName is null && part.Formality is null && part.Language is null && part.Signature is null)
            throw new ProfileValidationException(NothingToSaveMessage);
        var name = part.DisplayName is null ? null : CleanLine(part.DisplayName, "Your name", UserProfileLimits.DisplayName) ?? throw new ProfileValidationException(NothingToSaveMessage);
        var signature = part.Signature is null ? null : CleanSignature(part.Signature) ?? throw new ProfileValidationException(NothingToSaveMessage);
        var language = part.Language is null ? null : CleanLanguage(part.Language) ?? throw new ProfileValidationException(NothingToSaveMessage);
        if (part.Formality is { } chosen && (chosen == Formality.Unset || !Enum.IsDefined(chosen)))
            throw new ProfileValidationException(FormalityMessage);

        var result = await profiles.FillEmptyAsync(username, new UserProfileFill(name, part.Formality, language, signature), time.GetUtcNow(), ct);
        return new UserProfilePartResult(ToView(await profiles.GetAsync(username, ct)), result.Filled, result.AlreadySet);
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

    private static string? CleanLanguage(string? value)
    {
        var language = Clean(value, "Language", UserProfileLimits.Language)?.ToLowerInvariant();
        if (language is not null && !LanguageCode().IsMatch(language))
            throw new ProfileValidationException(LanguageMessage);
        return language;
    }

    private static string? CleanLine(string? value, string label, int limit)
    {
        var line = Clean(value, label, limit);
        if (line is not null && line.Any(IsSpecial))
            throw new ProfileValidationException(string.Format(CultureInfo.InvariantCulture, SingleLineFormat, label));
        return line;
    }

    private static string? CleanSignature(string? value)
    {
        var signature = Clean(value?.Replace("\r\n", "\n", StringComparison.Ordinal), "Signature", UserProfileLimits.Signature);
        if (signature is null)
            return null;
        if (signature.Any(c => c != '\n' && IsSpecial(c)))
            throw new ProfileValidationException(SignatureCharactersMessage);
        foreach (var line in signature.Split('\n'))
        {
            if (FieldNamePattern.Match(line) is { Success: true } field)
            {
                var found = Regex.Replace(field.Groups["label"].Value, "\\s+", " ");
                throw new ProfileValidationException(string.Format(CultureInfo.InvariantCulture, SignatureLabelFormat,
                    ProfilePrompt.Labels.FirstOrDefault(l => string.Equals(l, found, StringComparison.OrdinalIgnoreCase)) ?? found));
            }
        }
        return signature;
    }

    private static bool IsSpecial(char c) =>
        char.IsControl(c)
        || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        || (char.GetUnicodeCategory(c) == UnicodeCategory.Format && c is not (ZeroWidthNonJoiner or ZeroWidthJoiner));

    private const char ZeroWidthNonJoiner = '\u200C';
    private const char ZeroWidthJoiner = '\u200D';

    private static readonly Regex FieldNamePattern = new(
        "^\\s*(?<label>" + string.Join("|", ProfilePrompt.Labels.Select(l => Regex.Escape(l).Replace("\\ ", "\\s+", StringComparison.Ordinal))) + ")\\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
