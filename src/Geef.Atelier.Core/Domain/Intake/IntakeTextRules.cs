using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Lengths named in other units than words or characters, and questions that are really about the length.</summary>
public static class LengthMentions
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string Count =
        @"\d+|ein|eine|einen|einem|einer|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|halbe|halben|halber|" +
        @"one|two|three|four|five|six|seven|eight|nine|ten|half\s+a|a\s+half";

    private const string Unit =
        @"seiten?|sätzen?|satz|absätzen?|absatz|zeilen?|abschnitten?|abschnitt|" +
        @"pages?|sentences?|paragraphs?|lines?";

    private static readonly Regex Named = new(
        $@"(?<![\p{{L}}\p{{N}}])(?:(?:{Count})[\s-]+(?:{Unit})|one[\s-]?pager|einseitig\p{{L}}*|halbseitig\p{{L}}*|tweets?|einzeiler)(?![\p{{L}}\p{{N}}])",
        Options);

    private static readonly Regex LengthQuestion = new(
        @"(?<![\p{L}])(?:how\s+long|long|length|lang|länger|how\s+many\s+(?:words|pages|sentences|paragraphs|lines)|word\s+count|how\s+much\s+text|" +
        @"wie\s+lang\p{L}*|länge|wie\s+viele\s+(?:wörter|seiten|sätze|absätze|zeilen)|wortzahl|wie\s+viel\s+text|umfang)(?![\p{L}])",
        Options);

    private static readonly Regex WordCountOption = new(
        @"\d+\s*(?:words?|wörter|wörtern|pages?|seiten?|sätze|sentences?|zeichen|characters?)(?![\p{L}])",
        Options);

    /// <summary>True when <paramref name="requestText"/> names a length in pages, sentences, paragraphs or lines, or asks for a tweet.</summary>
    public static bool InRequest(string requestText) => !string.IsNullOrWhiteSpace(requestText) && Named.IsMatch(requestText);

    /// <summary>True when a proposed question asks how long the text should be, whatever topic the model gave it.</summary>
    public static bool AsksForLength(ProposedQuestion? question) =>
        question is not null
        && ((question.Text is { } text && LengthQuestion.IsMatch(text))
            || question.Options.Count(o => o is not null && WordCountOption.IsMatch(o)) >= 2);
}

/// <summary>
/// Content the clarifier must never show the person: links, web or mail addresses, and requests for access data or payment
/// secrets. The clarifier reads files the person did not write, so its output may carry instructions from them. Only the
/// question, option, assumption or summary is dropped, never the order.
/// </summary>
public static class IntakeContentGuard
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "txt", "md", "rtf", "odt", "xls", "xlsx", "csv", "ppt", "pptx", "jpg", "jpeg", "png", "gif", "json", "xml", "html", "htm", "eml", "msg", "zip",
    };

    private static readonly Regex Url = new(@"(?:[a-z][a-z0-9+.-]*://|www\.)\S", Options);
    private static readonly Regex Mail = new(@"[^\s@<>()\[\]]+@[^\s@<>()\[\]]+\.[^\s@<>()\[\]]+", Options);
    private static readonly Regex MarkdownLink = new(@"\[[^\]\n]*\]\([^)\n]*\)", Options);
    private static readonly Regex Domain = new(@"(?<![\p{L}\p{N}@.-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?<tld>[a-z]{2,24})(?![\p{L}\p{N}-])", Options);

    private const string Secret =
        @"passw(?:o|ö)rt(?:s|er|ern)?|kennw(?:o|ö)rt(?:s|er|ern)?|passwords?|passcodes?|passphrases?|pins?|pin-codes?|pin\s+codes?|tans?|otp|" +
        @"einmalcodes?|zugangsdaten|anmeldedaten|login-daten|logindaten|credentials?|login\s+details|" +
        @"api[\s-]?keys?|api[\s-]?schlüssel[ns]?|zugangsschlüssel[ns]?|private[rnsm]?\s+schlüssel[ns]?|private\s+keys?|secret\s+keys?|" +
        @"access\s+tokens?|zugangstokens?|sicherheitscodes?|security\s+codes?|cvv|cvc|" +
        @"(?:kredit)?kartennummer[n]?|(?:credit\s+)?card\s+numbers?|kontonummer[n]?|account\s+numbers?|iban|bankdaten|bank\s+details";

    private const string Ask =
        @"dein|deine|deinen|deinem|deiner|ihr|ihre|ihren|ihrem|ihrer|euer|eure|your|yours|" +
        @"nenn\p{L}*|send\p{L}*|schick\p{L}*|gib|geben|angeb\p{L}*|eingeb\p{L}*|teil\p{L}*\s+mit|mitteil\p{L}*|bestätig\p{L}*|verrat\p{L}*|" +
        @"enter|share|provide|confirm|give|type|tell|need|brauche|benötige";

    private static readonly Regex SecretWord = new($@"(?<![\p{{L}}\p{{N}}])(?:{Secret})(?![\p{{L}}\p{{N}}])", Options);
    private static readonly Regex AskWord = new($@"(?<![\p{{L}}\p{{N}}])(?:{Ask})(?![\p{{L}}\p{{N}}])", Options);

    /// <summary>True when <paramref name="text"/> contains a URL, a web domain, a mail address or a Markdown link (file names such as <c>notes.docx</c> do not count).</summary>
    public static bool HasLinkOrAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (Url.IsMatch(text) || Mail.IsMatch(text) || MarkdownLink.IsMatch(text))
            return true;
        return Domain.Matches(text).Any(m => !FileExtensions.Contains(m.Groups["tld"].Value));
    }

    /// <summary>True when <paramref name="text"/> asks for a password, PIN, TAN, key, token, card or account number (German and English); a mere mention without a request does not count.</summary>
    public static bool AsksForSecret(string? text) =>
        !string.IsNullOrWhiteSpace(text) && SecretWord.IsMatch(text) && AskWord.IsMatch(text);

    /// <summary>True when <paramref name="text"/> must not be shown: <see cref="HasLinkOrAddress"/> or <see cref="AsksForSecret"/>.</summary>
    public static bool IsUnsafe(string? text) => HasLinkOrAddress(text) || AsksForSecret(text);
}
