using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// Removes credential-like fragments from texts before they are logged or shown. A deny list, so it can only catch
/// common forms; texts for users should not carry technical details in the first place. Two levels:
/// <list type="bullet">
///   <item><see cref="RedactKnownSecrets"/>: unambiguous key forms only (known key prefixes such as <c>sk-</c>,
///   <c>tvly-</c>, <c>AIza</c>, <c>gsk_</c>, bearer/basic tokens, <c>Authorization:</c> headers, <c>name=value</c> or
///   <c>name: value</c> pairs with a secret-sounding name such as <c>key</c>, <c>api_key</c> or <c>x-api-key</c>, and
///   <c>API key provided: value</c>). Used for texts written for users (warnings), so long display names stay intact.</item>
///   <item><see cref="RedactSecrets"/>: additionally every long opaque token (32+ characters of letters, digits,
///   <c>_</c> and <c>-</c> with a letter and a digit, or base64 with <c>+</c> and <c>/</c> and mixed case and digits);
///   ids in GUID format are kept. Used for exception messages, upstream error bodies and log texts.</item>
/// </list>
/// Every pattern is a single character class or a fixed word sequence with atomic whitespace, so the run time stays
/// linear in the input length.
/// </summary>
public static partial class SecretRedactionExtensions
{
    /// <summary>Text that replaces a removed fragment.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Returns <paramref name="text"/> with every credential-like fragment replaced, including long opaque tokens.
    /// For exception messages, upstream answers and log texts.
    /// </summary>
    public static string RedactSecrets(this string? text)
    {
        var result = text.RedactKnownSecrets();
        if (result.Length == 0)
            return result;
        result = Base64Token().Replace(result, m => IsGuid(m.Value) || !HasMixedCaseAndDigit(m.Value) ? m.Value : Placeholder);
        result = OpaqueToken().Replace(result, m => IsGuid(m.Value) || !HasLetterAndDigit(m.Value) ? m.Value : Placeholder);
        return result;
    }

    /// <summary>
    /// Returns <paramref name="text"/> with unambiguous key forms replaced (known key prefixes, bearer tokens,
    /// authorization headers, secret-sounding <c>name=value</c> pairs, <c>API key provided: value</c>). Long words
    /// without such a form stay, so display names in texts for users are not cut.
    /// </summary>
    public static string RedactKnownSecrets(this string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = KnownKeyFormat().Replace(text, Placeholder);
        result = AuthorizationHeader().Replace(result, m => $"{m.Groups["name"].Value}{Placeholder}");
        result = BearerToken().Replace(result, m => $"{m.Groups["scheme"].Value} {Placeholder}");
        result = KeyProvided().Replace(result, m => $"{m.Groups["lead"].Value}{Placeholder}");
        result = NamedSecret().Replace(result, m => $"{m.Groups["name"].Value}{m.Groups["sep"].Value}{Placeholder}");
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

    private static bool HasMixedCaseAndDigit(string value) =>
        value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit);

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{10,}|\btvly-[A-Za-z0-9_\-]{10,}|\bAIza[0-9A-Za-z_\-]{20,}|\bgsk_[A-Za-z0-9]{20,}|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex KnownKeyFormat();

    [GeneratedRegex(@"(?<name>\b(?:proxy-)?authorization\b[""']?(?>[ \t]*)[:=](?>[ \t]*))[^\s""'][^\r\n""']*", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"(?<scheme>\b(?:Bearer|Basic|Token))(?>\s+)[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?<lead>\bkey(?>\s+)provided(?>\s*):?(?>\s*))[^\s""',;]{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex KeyProvided();

    [GeneratedRegex(@"(?<name>\b(?:api[_\-]?key|apikey|key|access[_\-]?token|refresh[_\-]?token|token|secret|client[_\-]?secret|password|passwd|pwd|authorization|auth)\b[""']?)(?<sep>(?>\s*)[:=](?>\s*)[""']?)[^\s""',;&)]{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{32,}={0,2}")]
    private static partial Regex Base64Token();

    [GeneratedRegex(@"[A-Za-z0-9_\-]{32,}")]
    private static partial Regex OpaqueToken();
}
