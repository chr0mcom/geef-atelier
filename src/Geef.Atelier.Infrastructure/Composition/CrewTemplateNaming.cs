using System.Text;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Builds the name for an auto-composed crew template. The slug is bounded so that the final
/// stored name — <c>custom-</c> prefix (added by <c>CrewService</c>) + slug + <c>-auto-&lt;timestamp&gt;</c>
/// + an optional <c>-N</c> dedup suffix — always fits the <c>Runs.CrewTemplateName</c> varchar(100)
/// column. Without this cap a long LLM-provided domain produced a 160-char name that materialized
/// into <c>CrewTemplates</c> (varchar(200)) but overflowed on the chained task run's insert,
/// silently breaking auto-crew chaining.
/// </summary>
internal static class CrewTemplateNaming
{
    /// <summary>Max length of the domain slug. Leaves headroom within varchar(100) for the
    /// <c>custom-</c> prefix (7), <c>-auto-</c> (6), the 12-char timestamp, and a dedup suffix.</summary>
    private const int MaxSlugLength = 60;

    /// <summary>Builds the unprefixed template name <c>{domain-slug}-auto-{yyyyMMddHHmm}</c>.</summary>
    public static string BuildAutoTemplateName(string domain) =>
        $"{Slugify(domain, MaxSlugLength)}-auto-{DateTimeOffset.UtcNow:yyyyMMddHHmm}";

    /// <summary>
    /// Lowercases, collapses every run of non-alphanumeric characters into a single hyphen, trims
    /// leading/trailing hyphens and caps the result at <paramref name="maxLength"/>. Unicode letters
    /// (incl. German umlauts) are preserved; only structural characters such as '/', '(' and
    /// whitespace are removed. Returns <c>"crew"</c> for empty input.
    /// </summary>
    internal static string Slugify(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "crew";

        var sb = new StringBuilder(text.Length);
        var lastWasHyphen = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (sb.Length > 0 && !lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > maxLength)
            slug = slug[..maxLength].Trim('-');

        return slug.Length == 0 ? "crew" : slug;
    }
}
