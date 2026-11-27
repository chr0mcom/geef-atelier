namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>Helpers for the texts of run warnings.</summary>
public static class RunWarningTextExtensions
{
    /// <summary>Cuts <paramref name="value"/> to <paramref name="maxLength"/> characters, ending with an ellipsis when cut.</summary>
    public static string Clip(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
