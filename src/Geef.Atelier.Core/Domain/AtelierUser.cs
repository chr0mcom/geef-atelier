namespace Geef.Atelier.Core.Domain;

/// <summary>
/// A user account (multi-user since Step20). <c>PasswordHash</c> is a BCrypt hash.
/// <c>IsAdmin</c> grants the admin role (system-wide run visibility, user/OAuth-client
/// management); <c>IsActive</c> false blocks login and revokes the user's OAuth tokens.
/// </summary>
public sealed record AtelierUser(
    string UserId,
    string Username,
    string PasswordHash,
    string? Email,
    bool IsActive,
    bool IsAdmin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
