namespace Geef.Atelier.Core.Domain.Profiles;

/// <summary>The values of a rendered profile block, without the fixed field names; <see cref="HasFreeText"/> when it gives "Never do this" or "Anything else".</summary>
public sealed record ProfileValues(IReadOnlyList<string> Values, bool HasFreeText)
{
    /// <summary>The values on their own lines, as one text.</summary>
    public string Text => string.Join('\n', Values);
}

/// <summary>Renders the profile for the clarifier and the writer.</summary>
public static class ProfilePrompt
{
    public const string NameLabel = "Name";
    public const string RoleLabel = "Role";
    public const string OrganizationLabel = "Organization";
    public const string FormalityLabel = "Address the readers";
    public const string LanguageLabel = "Preferred language (when the order names none)";
    public const string SignatureLabel = "Signature";
    public const string NoGosLabel = "Never do this";
    public const string FreeTextLabel = "Anything else";
    public const string Formally = "formally";
    public const string Informally = "informally";

    /// <summary>The field names in the order <see cref="Render"/> writes them.</summary>
    public static IReadOnlyList<string> Labels { get; } =
        [NameLabel, RoleLabel, OrganizationLabel, FormalityLabel, LanguageLabel, SignatureLabel, NoGosLabel, FreeTextLabel];
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
        Add(lines, NameLabel, profile.DisplayName);
        Add(lines, RoleLabel, profile.Role);
        Add(lines, OrganizationLabel, profile.Organization);
        if (profile.Formality != Formality.Unset)
            lines.Add(FormalityLabel + ": " + (profile.Formality == Formality.Formal ? Formally : Informally));
        Add(lines, LanguageLabel, profile.Language);
        if (!Blank(profile.Signature))
            lines.Add(SignatureLabel + ":\n" + profile.Signature!.Trim());
        Add(lines, NoGosLabel, profile.NoGos);
        Add(lines, FreeTextLabel, profile.FreeText);

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The values of a block written by <see cref="Render"/>, read back without its field names and without the fixed words of
    /// <see cref="FormalityLabel"/>. A field starts at a line that begins with a later field name than the one before and a colon;
    /// every other line belongs to the field above it. Lines before the first field name are kept as values, so nothing of an
    /// unknown block is lost. <c>null</c> or blank gives no values.
    /// </summary>
    public static ProfileValues Values(string? rendered)
    {
        var values = new List<string>();
        var hasFreeText = false;
        foreach (var (label, text) in Scan(rendered))
        {
            hasFreeText |= label is NoGosLabel or FreeTextLabel;
            if (label != FormalityLabel && text.Trim() is { Length: > 0 } value)
                values.Add(value);
        }
        return new ProfileValues(values, hasFreeText);
    }

    /// <summary>
    /// The field names of <see cref="Labels"/> that a block written by <see cref="Render"/> holds with a value, in the order of the block,
    /// read the same way as <see cref="Values"/>. <c>null</c>, blank or a block without field names gives none.
    /// </summary>
    public static IReadOnlyList<string> Fields(string? rendered) =>
        Scan(rendered).Where(f => f.Label is not null && f.Text.Trim().Length > 0).Select(f => f.Label!).Distinct(StringComparer.Ordinal).ToList();

    private static IEnumerable<(string? Label, string Text)> Scan(string? rendered)
    {
        var current = -1;
        var value = new List<string>();
        foreach (var line in (rendered ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var next = -1;
            for (var i = current + 1; i < Labels.Count; i++)
            {
                if (line.StartsWith(Labels[i] + ":", StringComparison.Ordinal))
                {
                    next = i;
                    break;
                }
            }
            if (next < 0)
            {
                value.Add(line);
                continue;
            }
            if (value.Count > 0)
                yield return (current >= 0 ? Labels[current] : null, string.Join('\n', value));
            value.Clear();
            current = next;
            value.Add(line[(Labels[next].Length + 1)..]);
        }
        if (value.Count > 0)
            yield return (current >= 0 ? Labels[current] : null, string.Join('\n', value));
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (!Blank(value))
            lines.Add(label + ": " + value!.Trim());
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
