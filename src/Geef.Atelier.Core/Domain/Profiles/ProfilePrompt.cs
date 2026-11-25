namespace Geef.Atelier.Core.Domain.Profiles;

/// <summary>Renders the profile for the clarifier and the writer.</summary>
public static class ProfilePrompt
{
    /// <summary>True when there is no profile or none of its fields is set.</summary>
    public static bool IsEmpty(UserProfileEntity? profile) =>
        profile is null
        || (profile.Formality == Formality.Unset
            && Blank(profile.DisplayName) && Blank(profile.Role) && Blank(profile.Organization) && Blank(profile.Language)
            && Blank(profile.Signature) && Blank(profile.NoGos) && Blank(profile.FreeText));

    /// <summary>Plain block "About the person" for clarifier and writer; null when empty. Fields in fixed order, labels in English.</summary>
    public static string? Render(UserProfileEntity? profile)
    {
        if (profile is null || IsEmpty(profile))
            return null;

        var lines = new List<string>();
        Add(lines, "Name", profile.DisplayName);
        Add(lines, "Role", profile.Role);
        Add(lines, "Organization", profile.Organization);
        if (profile.Formality != Formality.Unset)
            lines.Add("Address the readers: " + (profile.Formality == Formality.Formal ? "formally" : "informally"));
        Add(lines, "Preferred language (when the order names none)", profile.Language);
        if (!Blank(profile.Signature))
            lines.Add("Signature:\n" + profile.Signature!.Trim());
        Add(lines, "Never do this", profile.NoGos);
        Add(lines, "Anything else", profile.FreeText);

        return string.Join('\n', lines);
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (!Blank(value))
            lines.Add(label + ": " + value!.Trim());
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
