using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// Removes credential-like fragments from texts before they are logged or shown. A deny list, so it can only catch
/// common forms; texts for users should not carry technical details in the first place. Two levels:
/// <list type="bullet">
///   <item><see cref="RedactKnownSecrets"/>: unambiguous key forms only (known key prefixes such as <c>sk-</c>,
///   <c>tvly-</c>, <c>AIza</c>, <c>gsk_</c>, passwords in URLs, bearer/basic tokens, <c>Authorization:</c> headers,
///   <c>name=value</c>, <c>name: value</c> or <c>"name": "value"</c> pairs whose name ends in a secret-sounding word such
///   as <c>key</c>, <c>token</c>, <c>secret</c> or <c>password</c> (also <c>OPENAI_API_KEY</c>, <c>x-api-key</c>,
///   <c>apiKey</c>), and <c>API key provided: value</c>). Apart from URL query parameters (<c>?key=value</c>), a value
///   is only removed when it looks like a secret: at least 12 characters without whitespace with a letter and a digit
///   (or base64 padding). Ordinary words after such a name (<c>Key: Strategic goals</c>, <c>Basic research</c>) stay.
///   Used for texts written for users (warnings), so long display names stay intact.</item>
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
        result = UrlPassword().Replace(result, m => $"{m.Groups["lead"].Value}{Placeholder}");
        result = AuthorizationHeader().Replace(result, m =>
            m.Groups["value"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(LooksLikeSecret)
                ? $"{m.Groups["name"].Value}{Placeholder}"
                : m.Value);
        result = BearerToken().Replace(result, m =>
            LooksLikeSecret(m.Groups["value"].Value) ? $"{m.Groups["scheme"].Value} {Placeholder}" : m.Value);
        result = KeyProvided().Replace(result, m =>
            LooksLikeSecret(m.Groups["value"].Value) ? $"{m.Groups["lead"].Value}{Placeholder}" : m.Value);
        return result.RedactNamedSecrets();
    }

    private static string RedactNamedSecrets(this string text)
    {
        StringBuilder? builder = null;
        var copied = 0;
        var match = NamedSecret().Match(text);
        while (match.Success)
        {
            var value = match.Groups["value"];
            if (IsSecretName(match.Groups["id"].Value) && IsSecretValue(value.Value, IsQueryParameter(text, match)))
            {
                builder ??= new StringBuilder(text.Length);
                builder.Append(text, copied, value.Index - copied).Append(Placeholder);
                copied = value.Index + value.Length;
                match = NamedSecret().Match(text, copied);
            }
            else
            {
                match = NamedSecret().Match(text, value.Index);
            }
        }
        return builder is null ? text : builder.Append(text, copied, text.Length - copied).ToString();
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

    private const int MinimumSecretLength = 12;

    private static readonly string[] SecretNames =
        ["key", "apikey", "token", "secret", "password", "passwd", "pwd", "auth", "authorization", "credential", "credentials"];

    private static bool LooksLikeSecret(string value) =>
        value.Length >= MinimumSecretLength
        && value.Any(char.IsLetter)
        && (value.Any(char.IsDigit) || value.EndsWith('='));

    private static bool IsSecretValue(string value, bool isQueryParameter) =>
        isQueryParameter ? value.Length >= 4 : LooksLikeSecret(value);

    private static bool IsQueryParameter(string text, Match match) =>
        match.Index > 0
        && text[match.Index - 1] is '?' or '&'
        && match.Groups["sep"].Value == "=";

    private static bool IsSecretName(string identifier)
    {
        var segment = identifier[(identifier.LastIndexOfAny(['_', '-', '.']) + 1)..];
        for (var i = segment.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(segment[i]) && char.IsLower(segment[i - 1]))
            {
                segment = segment[i..];
                break;
            }
        }
        return SecretNames.Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasLetterAndDigit(string value) =>
        value.Any(char.IsLetter) && value.Any(char.IsDigit);

    private static bool HasMixedCaseAndDigit(string value) =>
        value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit);

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{10,}|\btvly-[A-Za-z0-9_\-]{10,}|\bAIza[0-9A-Za-z_\-]{20,}|\bgsk_[A-Za-z0-9]{20,}|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex KnownKeyFormat();

    [GeneratedRegex(@"(?<lead>://[^\s/:@]{1,256}:)[^\s/@]{1,256}(?=@)")]
    private static partial Regex UrlPassword();

    [GeneratedRegex(@"(?<name>(?<![A-Za-z0-9_\-])(?:proxy-)?authorization[""']?(?>[ \t]*)[:=](?>[ \t]*))(?<value>[^\s""'][^\r\n""']*)", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"(?<scheme>\b(?:Bearer|Basic|Token))(?>[ \t]+)(?<value>(?>[A-Za-z0-9._~+/=\-]+))", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?<lead>\bkey(?>[ \t]+)provided(?>[ \t]*):?(?>[ \t]*))(?<value>(?>[^\s""',;]+))", RegexOptions.IgnoreCase)]
    private static partial Regex KeyProvided();

    [GeneratedRegex(@"(?<name>(?<![A-Za-z0-9_\-.])(?<id>(?>[A-Za-z][A-Za-z0-9_\-.]*))[""']?)(?<sep>(?>[ \t]*)[:=](?>[ \t]*)[""']?)(?<value>(?>[^\s""',;&)=:]+)(?>={1,2}(?![^\s""',;&)]))?)")]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{32,}={0,2}")]
    private static partial Regex Base64Token();

    [GeneratedRegex(@"[A-Za-z0-9_\-]{32,}")]
    private static partial Regex OpaqueToken();
}
