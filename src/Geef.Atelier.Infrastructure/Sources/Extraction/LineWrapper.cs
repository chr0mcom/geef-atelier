using System.Text;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>Breaks lines that are longer than a limit, at the last space before the limit when there is one.</summary>
internal static class LineWrapper
{
    /// <summary>Returns <paramref name="text"/> with no line longer than <paramref name="maxLineChars"/>; <paramref name="wrapped"/> tells whether a line was broken.</summary>
    public static string Wrap(string text, int maxLineChars, out bool wrapped)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineChars, 20);

        wrapped = false;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Split('\n').All(l => l.Length <= maxLineChars))
            return normalized;

        var builder = new StringBuilder(normalized.Length + 64);
        var first = true;
        foreach (var line in normalized.Split('\n'))
        {
            if (!first)
                builder.Append('\n');
            first = false;

            var rest = line.AsSpan();
            while (rest.Length > maxLineChars)
            {
                wrapped = true;
                var window = rest[..(maxLineChars + 1)];
                var cut = window.LastIndexOf(' ');
                if (cut < maxLineChars / 2)
                {
                    cut = maxLineChars;
                    if (char.IsHighSurrogate(rest[cut - 1]))
                        cut--;
                    builder.Append(rest[..cut]).Append('\n');
                    rest = rest[cut..];
                }
                else
                {
                    builder.Append(rest[..cut].TrimEnd()).Append('\n');
                    rest = rest[(cut + 1)..];
                }
            }

            builder.Append(rest);
        }

        return builder.ToString();
    }
}
