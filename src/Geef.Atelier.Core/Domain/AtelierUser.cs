namespace Geef.Atelier.Core.Domain;

public sealed record AtelierUser(
    string UserId,
    string Username,
    string PasswordHash,
    string? Email,
    bool IsActive,
    bool IsAdmin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
