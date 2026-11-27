using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Mcp.Forms;

/// <summary>
/// Which texts may stand in a form Atelier sends to the MCP client (D-088). Questions, options and file names come from Atelier reading the
/// request and the files, so they may repeat text from a file; in a form they look as if Atelier itself asked. The form never takes typed text,
/// so nothing can be entered into it; these checks only keep such texts from being shown under Atelier's name. They find the common forms of
/// links, addresses, phone numbers, requests to call and words for secrets in German and English, not every possible one; a text they refuse
/// goes the usual way through <c>needs_input</c>.
/// </summary>
internal static partial class OrderFormText
{
    /// <summary>Longest question text shown in a form.</summary>
    public const int MaxQuestionChars = 240;

    /// <summary>Longest label of an answer option shown in a form.</summary>
    public const int MaxOptionChars = 120;

    /// <summary>Longest file name shown in a form.</summary>
    public const int MaxFileNameChars = 120;

    private static readonly string[] CompactSecrets =
    [
        "passwort", "password", "passwd", "kennwort", "passcode", "passphrase", "geheimzahl", "geheimnummer",
        "kartennummer", "kartenprüf", "prüfziffer", "kontonummer", "verfügernummer", "iban", "cvv", "cvc",
        "cardnumber", "creditcard", "debitcard", "accountnumber", "sortcode", "routingnumber",
        "zugangsdaten", "zugangscode", "anmeldedaten", "logindaten", "credential", "freischaltcode", "aktivierungscode",
        "sicherheitsfrage", "sicherheitscode", "securitycode", "securityquestion", "smscode", "bestätigungscode", "verifizierungscode",
        "verificationcode", "einmalcode", "onetimecode", "pushtan", "apikey", "accesskey", "privatekey", "secretkey", "seedphrase", "recoveryphrase",
        "motdepasse", "contraseña", "wachtwoord", "senha",
    ];

    /// <summary>
    /// True when <paramref name="text"/> is not blank, at most <paramref name="maxLength"/> characters long, in Latin script and none of the refusals
    /// applies: the guard of the intake (<see cref="IntakeContentGuard"/>) without any exception for what the request is about, and the link, number
    /// and word checks of this class on the normalized text. With <paramref name="fileName"/> the endings <c>.md</c> and <c>.zip</c> are allowed as the
    /// ending of the name.
    /// </summary>
    public static bool IsSafeForForm(this string? text, int maxLength, bool fileName = false)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength || !text.HasOnlyShownCharacters() || !text.IsLatinScript())
            return false;
        if (IntakeContentGuard.IsUnsafe(text, string.Empty))
            return false;
        var normalized = IntakeContentGuard.Normalize(text);
        var joined = SpacedLetters().Replace(normalized, string.Empty);
        if (joined.HasLink(fileName) || joined.HasPhoneOrCall() || SecretPattern().IsMatch(joined))
            return false;
        var compact = Compact(joined);
        return !CompactSecrets.Any(word => compact.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>True when every letter of the text is a Latin letter (forms are offered only for German and English).</summary>
    public static bool IsLatinScript(this string text)
    {
        foreach (var rune in IntakeContentGuard.Normalize(text).EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
                continue;
            if (rune.Value is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 0x00C0 and <= 0x024F) or (>= 0x1E00 and <= 0x1EFF)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// True when the text holds a link or markup that makes one, a web address written with "punkt" or "dot", a bare IP address or a name ending in
    /// <c>.md</c> or <c>.zip</c> (also web domains); mail addresses and ordinary domains are found by <see cref="IntakeContentGuard"/>.
    /// </summary>
    public static bool HasLink(this string text, bool fileName = false) =>
        LinkPattern().IsMatch(text) || (!fileName && FileLikeDomain().IsMatch(text));

    /// <summary>True when the text holds a phone number or another long number, a <c>tel:</c> or <c>sms:</c> link or asks to call, text or message someone.</summary>
    public static bool HasPhoneOrCall(this string text) => PhonePattern().IsMatch(text) || CallPattern().IsMatch(text);

    private static string Compact(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool HasOnlyShownCharacters(this string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
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

    [GeneratedRegex(@"(?<=(?<![\p{L}\p{N}])\p{L})[\s.\-_·]+(?=\p{L}(?![\p{L}\p{N}]))", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SpacedLetters();

    [GeneratedRegex(
        @"://|\bwww\.|\bmailto:|\btel:|\bsms:|\]\(|<\s*/?\s*a\b|href\s*=|@[\p{L}\p{N}-]+\.[\p{L}]{2,}" +
        @"|[\p{L}\p{N}-]+\s*[\(\[]?\s*(?:punkt|dot)\s*[\)\]]?\s*(?:de|com|net|org|eu|at|ch|io|info|biz|co|me|app|online|shop)\b" +
        @"|(?<![\p{N}.])\d{1,3}(?:\.\d{1,3}){3}(?![\p{N}.])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"[\p{L}\p{N}-]+\.(?:md|zip|mov)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex FileLikeDomain();

    [GeneratedRegex(@"(?:\+\s*)?\d(?:[\s\-/()]*\d){6,}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(
        @"\banruf|\brufen\s+sie\b|\bruf\s+(?:uns|mich|an)\b|\brückruf|\bcall\s+(?:us|me|this|the|our|now)\b|\bcall\s+\+|\bphone\b|\btelefon|\bhandy|\bmobilnummer" +
        @"|\bwhatsapp|\bsms\b|\bper\s+sms|\btext\s+(?:us|me)\b|\d+\s*-?\s*(?:stellig|digit)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex CallPattern();

    [GeneratedRegex(
        @"\bpins?\b|\bpin-|\btans?\b|\btan-|\bpuk\b|\bbic\b|\bswift\b|\bcvv\d?\b|\bcvc\d?\b|\botp\b|\b2fa\b|\btoken|\bsecret|\bgeheim" +
        @"|\blog\s?in\b|\blog-in\b|\bschlüssel|\bsozialversicherungsnummer|\bsteuer-?id|\btax\s+id|\bausweisnummer|\bpassport\s+number|\breisepassnummer" +
        @"|\bbank\s+details|\bbankverbindung|\bbankdaten|\bcard\s+number|\baccount\s+number|\bcredit\s+card|\bkreditkart",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex SecretPattern();
}
