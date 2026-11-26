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

/// <summary>A field a result can ask for (name, form of address, language, signature); it is only ever filled while it is empty.</summary>
public enum ProfileFillField { DisplayName = 0, Formality = 1, Language = 2, Signature = 3 }

/// <summary>Values to put into the empty fields of a profile; <c>null</c> (and <see cref="Formality.Unset"/>) asks for nothing.</summary>
public sealed record UserProfileFill(string? DisplayName, Formality? Formality, string? Language, string? Signature);

/// <summary>Which of the asked fields were filled and which already held a value and stayed as they were.</summary>
public sealed record UserProfileFillResult(IReadOnlyList<ProfileFillField> Filled, IReadOnlyList<ProfileFillField> AlreadySet);

/// <summary>Longest accepted value per profile field, in characters.</summary>
public static class UserProfileLimits
{
    public const int DisplayName = 200, Role = 200, Organization = 200, Language = 10, Signature = 1_000, NoGos = 2_000, FreeText = 4_000;
}
