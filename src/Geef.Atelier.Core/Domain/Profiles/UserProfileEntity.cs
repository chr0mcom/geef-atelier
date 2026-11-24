namespace Geef.Atelier.Core.Domain.Profiles;

/// <summary>How the readers of a text are addressed.</summary>
public enum Formality { Unset = 0, Formal = 1, Informal = 2 }

/// <summary>What a person tells about themselves once ("About me"); strictly per user, keyed by the user name.</summary>
public sealed record UserProfileEntity
{
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Role { get; init; }
    public string? Organization { get; init; }
    public Formality Formality { get; init; }

    /// <summary>Preferred text language as ISO 639-1 code, used only when the order names none.</summary>
    public string? Language { get; init; }

    public string? Signature { get; init; }
    public string? NoGos { get; init; }
    public string? FreeText { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Longest accepted value per profile field, in characters.</summary>
public static class UserProfileLimits
{
    public const int DisplayName = 200, Role = 200, Organization = 200, Language = 10, Signature = 1_000, NoGos = 2_000, FreeText = 4_000;
}
