using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// Removes credential-like fragments from technical texts (exception messages, upstream error bodies) before they are
/// logged or shown. Known key formats, bearer tokens, <c>name=value</c> pairs with a secret-sounding name and long
/// opaque tokens are replaced with <see cref="Placeholder"/>; ids in GUID format are kept.
/// </summary>
public static partial class SecretRedactionExtensions
{
    /// <summary>Text that replaces a removed fragment.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>Returns <paramref name="text"/> with every credential-like fragment replaced.</summary>
    public static string RedactSecrets(this string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = KnownKeyFormat().Replace(text, Placeholder);
        result = BearerToken().Replace(result, m => $"{m.Groups["scheme"].Value} {Placeholder}");
        result = NamedSecret().Replace(result, m => $"{m.Groups["name"].Value}{m.Groups["sep"].Value}{Placeholder}");
        result = OpaqueToken().Replace(result, m => IsGuid(m.Value) || !HasLetterAndDigit(m.Value) ? m.Value : Placeholder);
        return result;
    }

    /// <summary>
    /// Type, redacted message and stack trace of <paramref name="exception"/> and its inner exceptions, for a log entry
    /// that must not carry credentials from an upstream error text.
    /// </summary>
    public static string ToRedactedLogText(this Exception exception)
    {
        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
                builder.AppendLine().Append(" ---> ");
            builder.Append(current.GetType().FullName).Append(": ").Append(current.Message.RedactSecrets());
            if (current.StackTrace is { } stack)
                builder.AppendLine().Append(stack);
        }
        return builder.ToString();
    }

    private static bool IsGuid(string value) => Guid.TryParse(value, out _);

    private static bool HasLetterAndDigit(string value) =>
        value.Any(char.IsLetter) && value.Any(char.IsDigit);

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{10,}|\btvly-[A-Za-z0-9_\-]{10,}|\bAIza[0-9A-Za-z_\-]{20,}|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex KnownKeyFormat();

    [GeneratedRegex(@"(?<scheme>\b(?:Bearer|Basic|Token))\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?<name>\b(?:api[_\-]?key|apikey|key|access[_\-]?token|refresh[_\-]?token|token|secret|client[_\-]?secret|password|passwd|pwd|authorization|auth)\b[""']?)(?<sep>\s*[:=]\s*[""']?)[^\s""',;&)]{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"[A-Za-z0-9_\-]{32,}")]
    private static partial Regex OpaqueToken();
}
