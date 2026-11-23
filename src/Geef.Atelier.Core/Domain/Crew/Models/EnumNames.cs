namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Strict enum-name parsing shared by the model-matrix parsers and the persistence converter (D-066). Unlike
/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> it accepts exactly one declared member name (trimmed,
/// case-insensitive) and rejects numbers, signs and comma-separated combinations such as <c>"General,Legal"</c>.
/// </summary>
public static class EnumNames
{
    /// <summary>The declared member of <typeparamref name="TEnum"/> named <paramref name="raw"/>, or <c>null</c>.</summary>
    public static TEnum? ParseExact<TEnum>(string? raw)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var name = raw.Trim();
        foreach (var declared in Enum.GetNames<TEnum>())
        {
            if (string.Equals(declared, name, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<TEnum>(declared);
        }

        return null;
    }
}
