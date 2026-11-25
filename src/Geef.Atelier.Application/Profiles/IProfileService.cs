using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Application.Profiles;

/// <summary>The values of the profile form.</summary>
public sealed record UserProfileInput(string? DisplayName, string? Role, string? Organization, Formality Formality, string? Language, string? Signature, string? NoGos, string? FreeText);

/// <summary>The profile as shown on "About me".</summary>
public sealed record UserProfileView(UserProfileInput Values, bool IsEmpty, DateTimeOffset? UpdatedAt);

/// <summary>Raised for profile values the person can fix; the message is plain English.</summary>
public sealed class ProfileValidationException(string message) : InvalidOperationException(message);

/// <summary>The person's own profile. Always bound to the signed-in user; there is no admin access to other profiles.</summary>
public interface IProfileService
{
    /// <summary>Returns the profile of <paramref name="username"/>; an empty view when there is none.</summary>
    Task<UserProfileView> GetAsync(string username, CancellationToken ct = default);

    /// <summary>Trims, turns blanks into null, checks the limits of <see cref="UserProfileLimits"/> and a two-letter language code; throws <see cref="ProfileValidationException"/>.</summary>
    Task<UserProfileView> SaveAsync(string username, UserProfileInput input, CancellationToken ct = default);

    /// <summary>Deletes the profile of <paramref name="username"/>.</summary>
    Task ClearAsync(string username, CancellationToken ct = default);
}
