namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Tolerant parser for <see cref="ModelSlot"/> names coming from composer JSON or settings dictionaries.</summary>
public static class ModelSlotParser
{
    /// <summary>
    /// Parses <paramref name="raw"/> ignoring case, hyphens, underscores and spaces
    /// (<c>"fact_reviewer"</c>, <c>"Fact-Reviewer"</c> and <c>"FactReviewer"</c> are equal).
    /// Only a single slot name is accepted: numbers and combined values such as <c>"FactReviewer,StyleReviewer"</c>
    /// (which <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would OR together) are rejected.
    /// </summary>
    public static bool TryParse(string? raw, out ModelSlot slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var compact = new string(raw.Where(c => c is not ('-' or '_' or ' ')).ToArray());
        if (EnumNames.ParseExact<ModelSlot>(compact) is not { } parsed)
            return false;

        slot = parsed;
        return true;
    }

    /// <summary>Parses <paramref name="raw"/> or returns <c>null</c>.</summary>
    public static ModelSlot? ParseOrNull(string? raw) => TryParse(raw, out var slot) ? slot : null;
}
