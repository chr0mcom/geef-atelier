namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>Helpers that turn technical error texts into short, single-line details for warning messages.</summary>
public static class RunWarningTextExtensions
{
    /// <summary>Maximum length of a technical detail embedded in a warning message.</summary>
    public const int DetailLength = 200;

    /// <summary>First line of <paramref name="text"/>, trimmed and cut to <see cref="DetailLength"/> characters.</summary>
    public static string ToWarningDetail(this string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "no details";
        var firstLine = text.Trim().Split('\n', 2)[0].Trim().TrimEnd('\r');
        return firstLine.Length <= DetailLength ? firstLine : firstLine[..(DetailLength - 1)] + "…";
    }

    /// <summary>Cuts <paramref name="value"/> to <paramref name="maxLength"/> characters, ending with an ellipsis when cut.</summary>
    public static string Clip(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
