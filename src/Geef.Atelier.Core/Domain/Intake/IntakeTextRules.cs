using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Lengths named in other units than words or characters, and questions that are really about the length of the text.</summary>
public static class LengthMentions
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string WriteVerb =
        @"schreib\p{L}*|verfass\p{L}*|formulier\p{L}*|entwirf|entwerf\p{L}*|erstell\p{L}*|fass\p{L}*|zusammenfass\p{L}*|" +
        @"beschreib\p{L}*|erklär\p{L}*|antworte|beantworte|brauch\p{L}*|bräucht\p{L}*|möcht\p{L}*|hätte|mach\p{L}*|" +
        @"write|draft|compose|create|summari[sz]e|describe|explain|reply|respond|need|want|give|make|prepare|turn";

    private const string Request = WriteVerb + @"|bitte|please|pls";

    private const string Filler = @"mir|uns|me|us|bitte|please|pls|noch|kurz|mal|nur|only|just";

    private const string Limit =
        @"max(?:imal|imum)?\.?|höchstens|etwa|ca\.?|ungefähr|rund|knapp|genau|nur|about|around|roughly|approximately|exactly|only|just|" +
        @"no\s+more\s+than|at\s+most|up\s+to|bis\s+zu|under|unter|less\s+than|weniger\s+als";

    private const string Count =
        @"\d+|ein|eine|einen|einem|einer|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|ein\s+paar|wenigen?|einigen?|" +
        @"a|an|one|two|three|four|five|six|seven|eight|nine|ten|half\s+a|a\s+few|few|a\s+couple\s+of|some";

    private const string Adjective =
        @"halbe|halben|halber|halbes|ganze|ganzen|kurze|kurzen|kurzer|knappe|knappen|einzige|einzigen|einzelne|einzelnen|" +
        @"short|brief|single|full|whole";

    private const string Unit =
        @"seiten?|sätzen?|sätze|satz|absätzen?|absätze|absatz|zeilen?|abschnitten?|abschnitte|abschnitt|" +
        @"pages?|sentences?|paragraphs?|lines?";

    private const string Preposition = @"in|auf|within|on|mit|als|as|von|of";

    private const string NotOwnText = @"(?!\s*(?:des|der|vom|von|of|from)(?![\p{L}\p{N}]))";

    private static readonly string Amount = $@"(?:(?:{Limit})\s+)?(?:{Count})\s+(?:(?:{Adjective})[\s,]+){{0,2}}(?:{Unit})(?![\p{{L}}\p{{N}}]){NotOwnText}";

    private static readonly Regex ObjectOfVerb = new(
        $@"(?<![\p{{L}}\p{{N}}])(?:{WriteVerb})(?![\p{{L}}\p{{N}}])\s+(?:(?:{Filler})\s+)*{Amount}",
        Options);

    private static readonly Regex AfterPreposition = new(
        $@"(?<![\p{{L}}\p{{N}}])(?:{Preposition})\s+{Amount}",
        Options);

    private static readonly Regex Standalone = new(
        @"(?<![\p{L}\p{N}])(?:one[\s-]?pager|one[\s-]?liner|einzeiler|(?:one|two|half)-page)(?![\p{L}\p{N}])",
        Options);

    private static readonly Regex ShapeAfterVerb = new(
        $@"(?<![\p{{L}}\p{{N}}])(?:{WriteVerb})(?![\p{{L}}\p{{N}}])\s+(?:(?:{Filler})\s+)*(?:(?:a|an|one|ein|eine|einen|nen)\s+)?" +
        @"(?:(?:kurze[nmrs]?|witzige[nmrs]?|short|funny)\s+)?(?:tweets?|einseitig\p{L}*|halbseitig\p{L}*|zweiseitig\p{L}*)(?![\p{L}\p{N}])",
        Options);

    private static readonly Regex RequestWord = new($@"(?<![\p{{L}}\p{{N}}])(?:{Request})(?![\p{{L}}\p{{N}}])", Options);

    private static readonly Regex SentenceEnd = new(@"(?<=[.!?;])\s+(?=\p{Lu})|\n+", RegexOptions.CultureInvariant);

    private static readonly Regex LengthWord = new(
        @"(?<![\p{L}\p{N}])(?:how\s+long|long|longer|length|lang|lange|langen|langer|langes|länger|längere[nmrs]?|länge|umfang|umfangs|" +
        @"how\s+many\s+(?:pages|sentences|paragraphs|lines)|wie\s+viele\s+(?:seiten|sätze|absätze|zeilen))(?![\p{L}\p{N}-])",
        Options);

    private static readonly Regex WordCount = new(
        @"(?<![\p{L}\p{N}])(?:how\s+many\s+(?:words|characters)|word\s+count|how\s+much\s+text|wie\s+viele\s+(?:wörter|zeichen)|wortzahl|wortanzahl|wie\s+viel\s+text)(?![\p{L}\p{N}])",
        Options);

    private static readonly Regex TextReference = new(
        @"(?<![\p{L}\p{N}])(?:\p{L}*(?:text|texte|textes|texts|beitrag|beitrags|brief|briefe|briefes|briefs|mail|nachricht|artikel|post|posting|rede|" +
        @"anschreiben|einladung|ankündigung|antwort|bericht|zusammenfassung|kommentar|absage|bewerbung|newsletter|entwurf|dokument)|" +
        @"letter|email|e-mail|message|article|piece|essay|speech|reply|answer|summary|draft|document|copy|invitation|announcement|bio|" +
        @"description|blurb|note|caption|story)(?![\p{L}\p{N}])|" +
        @"(?:should|will|would|could|can|must|may|shall)\s+(?:it|this|that)\s+be(?![\p{L}])|(?:like|want)\s+it\s+to\s+be(?![\p{L}])|" +
        @"(?:soll|sollte|sollen|darf|dürfen|wird|muss|kann)\s+(?:es|er|sie|das)\s+(?:(?:ungefähr|etwa|eher|denn|insgesamt)\s+)?(?:sein|werden)(?![\p{L}])",
        Options);

    private static readonly Regex Wish = new(
        @"(?<![\p{L}\p{N}])(?:should|shall|would|want|prefer|like|ideally|aim|soll\p{L}*|möcht\p{L}*|wünsch\p{L}*|willst|wollen|darf|dürfen|hätte\p{L}*)(?![\p{L}\p{N}])",
        Options);

    private static readonly Regex WordCountOption = new(
        @"\d+\s*(?:words?|wörter|wörtern|pages?|seiten?|sätze|sentences?|zeichen|characters?)(?![\p{L}])",
        Options);

    /// <summary>
    /// True when <paramref name="requestText"/> names a length in pages, sentences, paragraphs or lines, or asks for a tweet, and
    /// the unit clearly belongs to the writing task: as the object of a writing verb ("write two paragraphs"), after a preposition
    /// in a sentence that asks for a text ("in three sentences"), or as a fixed shape ("one-pager"). A unit that names something
    /// else ("ten pages are missing", "two pages of the contract", "the tweet of a customer") does not count.
    /// </summary>
    public static bool InRequest(string requestText)
    {
        if (string.IsNullOrWhiteSpace(requestText))
            return false;
        if (Standalone.IsMatch(requestText))
            return true;
        foreach (var sentence in SentenceEnd.Split(requestText))
        {
            if (ObjectOfVerb.IsMatch(sentence) || ShapeAfterVerb.IsMatch(sentence))
                return true;
            if (RequestWord.IsMatch(sentence) && AfterPreposition.IsMatch(sentence))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when a proposed question asks how long the text should be: it names the length of the text itself ("How long should the
    /// post be?", "Welchen Umfang soll der Text haben?"), asks for a word count, or offers at least two word counts. "Long", "lang" or
    /// "Umfang" alone, without the text, do not count ("How long have you worked there?", "Welchen Umfang hat der Schaden?").
    /// </summary>
    public static bool AsksForTextLength(ProposedQuestion? question)
    {
        if (question is null)
            return false;
        if (question.Options.Count(o => o is not null && WordCountOption.IsMatch(o)) >= 2)
            return true;
        if (question.Text is not { } text)
            return false;
        return WordCount.IsMatch(text) || (LengthWord.IsMatch(text) && TextReference.IsMatch(text) && Wish.IsMatch(text));
    }
}

/// <summary>
/// Content the clarifier must never show the person: links, web or mail addresses, and access data or payment secrets that the
/// request of the person does not treat (a request for such a secret never). The clarifier reads files the person did not write, so its output may carry instructions from them. Only the
/// question, option, assumption or summary is dropped, never the order. Texts are normalized first (NFKC, format characters
/// removed, odd dots such as <c>[.]</c> or <c>。</c> read as dots).
/// </summary>
public static class IntakeContentGuard
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "txt", "md", "rtf", "odt", "xls", "xlsx", "csv", "ppt", "pptx", "jpg", "jpeg", "png", "gif", "json", "xml", "html", "htm", "eml", "msg", "zip",
    };

    private static readonly Regex OddDot = new(@"\s?[\[\(\{]\s*(?:\.|dot|punkt)\s*[\]\)\}]\s?|[。｡︒]", Options);
    private static readonly Regex Url = new(@"(?:[a-z][a-z0-9+.-]*://|www\.|(?<![\p{L}\p{N}:/])//[\p{L}\p{N}])\S?", Options);
    private static readonly Regex Mail = new(@"[^\s@<>()\[\]]+@[^\s@<>()\[\]]+\.[^\s@<>()\[\]]+", Options);
    private static readonly Regex MarkdownLink = new(@"\[[^\]\n]*\]\([^)\n]*\)|\[[^\]\n]+\]\[[^\]\n]*\]|^\s*\[[^\]\n]+\]:\s*\S", Options | RegexOptions.Multiline);
    private static readonly Regex IpWithPath = new(@"(?<![\p{N}.])\d{1,3}(?:\.\d{1,3}){3}(?:[/\\]|:\d|[?#][\p{L}\p{N}=&])", Options);
    private static readonly Regex Domain = new(
        @"(?<![\p{L}\p{N}@._-])(?:[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?\.)+(?<tld>\p{L}{2,24}|xn--[a-z0-9-]{1,59})(?![\p{L}\p{N}_-])(?<path>[/\\]|:\d|[?#][\p{L}\p{N}=&])?",
        Options);

    private const string SecretInWord =
        @"passw[oö]rt|kennw[oö]rt|password|passcode|passphrase|passkey|geheimzahl|geheimnummer|zugangsdaten|anmeldedaten|logindaten|" +
        @"login-daten|zugangscode|sicherheitscode|einmalcode|credential|kartennummer|kontonummer|pinnummer|pincode|tannummer";

    private const string SecretWord =
        @"otp|cvv|cvc|iban|api[\s-]?keys?|api[\s-]?schlüssel\p{L}*|zugangsschlüssel\p{L}*|private[rnsm]?\s+schlüssel\p{L}*|private\s+keys?|" +
        @"secret\s+keys?|access\s+tokens?|zugangstokens?|security\s+codes?|login\s+details|(?:credit\s+)?card\s+numbers?|account\s+numbers?|" +
        @"bankdaten|bank\s+details|pin[\s-]codes?|pin-nummer|tan-nummer|tan-liste";

    private const string Ask =
        @"dein|deine|deinen|deinem|deiner|ihr|ihre|ihren|ihrem|ihrer|euer|eure|your|yours|" +
        @"nenn\p{L}*|send\p{L}*|schick\p{L}*|gib|geben|angeb\p{L}*|eingeb\p{L}*|teil\p{L}*\s+mit|mitteil\p{L}*|bestätig\p{L}*|verrat\p{L}*|" +
        @"lautet|lauten|heißt|heisst|trag\p{L}*|eintrag\p{L}*|ergänz\p{L}*|einfüg\p{L}*|schreib\p{L}*|notier\p{L}*|hinterleg\p{L}*|welche[snmr]?|" +
        @"enter|share|provide|confirm|give|type|tell|need|put|write|add|insert|paste|fill|which|what\s+is|what's|brauche|benötige";

    private static readonly Regex Secret = new(
        $@"(?:{SecretInWord})|(?<![\p{{L}}\p{{N}}])(?:{SecretWord})(?![\p{{L}}\p{{N}}])",
        Options);

    private static readonly Regex UpperCaseSecret = new(
        @"(?<!\p{Lu})(?:PIN|TAN)s?(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant);

    private static readonly Regex AskWord = new($@"(?<![\p{{L}}\p{{N}}])(?:{Ask})(?![\p{{L}}\p{{N}}])", Options);

    /// <summary>The text as the rules read it: NFKC, without format characters (zero-width and similar), odd dots as <c>.</c>.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var folded = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                builder.Append(c);
        }
        return OddDot.Replace(builder.ToString(), ".");
    }

    /// <summary>
    /// True when <paramref name="text"/> contains a URL, a web domain, an IP address with a path, a mail address or a Markdown link.
    /// A bare name such as <c>Booking.com</c> or <c>Node.js</c> is allowed when it stands literally in <paramref name="requestText"/>
    /// (the words of the person, never file contents); a file name such as <c>notes.docx</c> is allowed without a path.
    /// Schemes, <c>www.</c>, paths, mail addresses and Markdown links never are.
    /// </summary>
    public static bool HasLinkOrAddress(string? text, string? requestText = null)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (Url.IsMatch(normalized) || Mail.IsMatch(normalized) || MarkdownLink.IsMatch(normalized) || IpWithPath.IsMatch(normalized))
            return true;

        HashSet<string>? named = null;
        foreach (Match match in Domain.Matches(normalized))
        {
            if (match.Groups["path"].Success)
                return true;
            var name = match.Value;
            if (name.Count(c => c == '.') == 1 && FileExtensions.Contains(match.Groups["tld"].Value))
                continue;
            named ??= NamesIn(requestText);
            if (!named.Contains(name))
                return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="text"/> mentions a password, PIN, TAN, key, token, card or account number in any form (also inside compound words such as <c>Firmenkennwort</c>).</summary>
    public static bool MentionsSecret(string? text)
    {
        var normalized = Normalize(text);
        return normalized.Length > 0 && (Secret.IsMatch(normalized) || UpperCaseSecret.IsMatch(normalized));
    }

    /// <summary>True when <paramref name="text"/> asks for a secret: it mentions one (<see cref="MentionsSecret"/>) together with a word that asks, points to the reader or wants it written down.</summary>
    public static bool AsksForSecret(string? text) => MentionsSecret(text) && AskWord.IsMatch(Normalize(text));

    /// <summary>
    /// True when <paramref name="text"/> must not be shown: it has a link or address (<see cref="HasLinkOrAddress"/>), asks for a secret
    /// (<see cref="AsksForSecret"/>, always), or mentions a secret although the request of the person (<paramref name="requestText"/>)
    /// does not treat the topic.
    /// </summary>
    public static bool IsUnsafe(string? text, string? requestText = null) =>
        HasLinkOrAddress(text, requestText)
        || AsksForSecret(text)
        || (MentionsSecret(text) && !MentionsSecret(requestText));

    private static HashSet<string> NamesIn(string? requestText)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Domain.Matches(Normalize(requestText)))
        {
            if (!match.Groups["path"].Success)
                names.Add(match.Value);
        }
        return names;
    }
}
