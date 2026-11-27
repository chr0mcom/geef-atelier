using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Mcp.Forms;

/// <summary>
/// Which texts may stand in a form Atelier sends to the MCP client (D-088). Questions, options and file names come from Atelier reading the
/// request and the files, so they may repeat text from a file; in a form they look as if Atelier itself asked. A text that sounds like it asks for
/// a secret, holds a link or an address, holds characters that change how text is shown, or is too long, is refused, and the questions go the
/// usual way through <c>needs_input</c>.
/// </summary>
internal static partial class OrderFormText
{
    /// <summary>Longest question text shown in a form.</summary>
    public const int MaxQuestionChars = 240;

    /// <summary>Longest label of an answer option shown in a form.</summary>
    public const int MaxOptionChars = 120;

    /// <summary>Longest file name shown in a form.</summary>
    public const int MaxFileNameChars = 120;

    /// <summary>
    /// True when <paramref name="text"/> is not blank, at most <paramref name="maxLength"/> characters long and none of the refusals applies: the
    /// guard of the intake (<see cref="IntakeContentGuard"/>) without any exception for what the request is about, and the stricter word and link
    /// lists of this class.
    /// </summary>
    public static bool IsSafeForForm(this string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Length <= maxLength
        && text.HasOnlyShownCharacters(allowLineBreaks: false)
        && !IntakeContentGuard.IsUnsafe(text, string.Empty)
        && !text.HasLink()
        && !text.SoundsLikeASecret();

    /// <summary>True when an answer typed into the form has only characters that are shown as they are (line breaks and tabs allowed).</summary>
    public static bool IsSafeAnswer(this string text) => text.HasOnlyShownCharacters(allowLineBreaks: true);

    /// <summary>
    /// True when the text holds a link or markup that makes one; bare web and mail addresses are found by <see cref="IntakeContentGuard"/>, which
    /// knows file names such as <c>offer.pdf</c>.
    /// </summary>
    public static bool HasLink(this string text) => LinkPattern().IsMatch(text);

    /// <summary>True when the text names a password, PIN, TAN, key, card or account number or another secret (English and German words).</summary>
    public static bool SoundsLikeASecret(this string text) => SecretPattern().IsMatch(text);

    private static bool HasOnlyShownCharacters(this string text, bool allowLineBreaks)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (allowLineBreaks && rune.Value is '\n' or '\r' or '\t')
                continue;
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.OtherNotAssigned:
                    return false;
            }
        }

        return true;
    }

    [GeneratedRegex(
        @"://|\bwww\.|\bmailto:|\]\(|<\s*/?\s*a\b|href\s*=|@[\p{L}\p{N}-]+\.[\p{L}]{2,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(
        @"passw|password|passwort|kennwort|passcode|pass code|passphrase|\bpins?\b|\bpin-|\btans?\b|\btan-|\bpuk\b|\biban\b|\bbic\b|\bswift\b|\bcvv\b|\bcvc\b" +
        @"|card number|credit card|debit card|kartennummer|kreditkart|karten-?nr|kontonummer|konto-?nr|account number|bank details|bankverbindung|bankdaten" +
        @"|sort code|routing number|api[- ]?key|access key|secret|geheim|token|zugangsdaten|anmeldedaten|login|log-in|log in|credentials|private key|schlüssel" +
        @"|social security|sozialversicherungsnummer|steuer-?id|tax id|ausweisnummer|passport number|reisepassnummer|\b2fa\b|one-time code|einmalcode|\botp\b" +
        @"|bestätigungscode|verification code|security code|sicherheitscode|seed phrase|recovery phrase|wiederherstellungs",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex SecretPattern();
}
