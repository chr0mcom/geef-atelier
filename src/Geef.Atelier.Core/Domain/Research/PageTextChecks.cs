namespace Geef.Atelier.Core.Domain.Research;

/// <summary>Checks on text taken from other web pages.</summary>
public static class PageTextChecks
{
    /// <summary>
    /// True when <paramref name="text"/> holds a control character other than tab and line breaks, or a character that changes the
    /// direction of the text (bidi embeddings, overrides, isolates and marks). Such a passage could be shown differently from what the
    /// writer and the fact check read, so the research drops it.
    /// </summary>
    public static bool HasHiddenControls(this string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r'))
                return true;
            if (c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩'))
                return true;
        }
        return false;
    }
}
