using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>
/// Tries to recognize, without an AI model, research topics that hold a contact or account detail of a person: an email address, a
/// phone number, an IBAN or another long or labelled account number, a date of birth, a street with a house number or a postcode with
/// a town. Such a topic is withheld by <see cref="ResearchQueryGate"/>, whatever the request says. It does not recognize every form.
/// </summary>
/// <remarks>
/// The rules are narrow on purpose, so that years, lists of years, paragraphs, amounts written with separators, rankings, durations and
/// the numbers of norms still go out. A number counts from seven digits in a row; from seven digits when it starts with <c>0</c> or
/// <c>+</c>, also in brackets and split by spaces, dots, slashes, hyphens or dashes; from ten digits in at least three groups that are
/// not all years; and from six digits right after a word for an account, customer or contract number. A date counts as a date of
/// birth only with a word for birth in the topic. A house number follows a street word that ends a longer word, or a short street name
/// after "Am", "An der" and the like; it is never a year from 1900 to 2099 and never a number followed by a unit or a count. A
/// five-digit postcode counts only before a word in capitals that is not a unit or a count, and never after the name of a norm. Names,
/// health details and other personal details in plain words are not recognized; the texts for the person say so.
/// </remarks>
public static partial class ResearchTopicPrivacy
{
    /// <summary>Fewest digits of a number that counts as a phone, account or customer number.</summary>
    public const int MinNumberDigits = 7;

    /// <summary>Fewest digits of a number in three or more groups that counts as an account number without a leading zero.</summary>
    public const int MinGroupedDigits = 10;

    /// <summary>Fewest digits of a number right after a word for an account, customer or contract number.</summary>
    public const int MinLabelledDigits = 6;

    private static readonly HashSet<string> CountWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Euro", "EUR", "Dollar", "USD", "CHF", "Franken", "Pfund", "GBP", "Prozent", "Einwohner", "Einwohnern", "Mitglieder",
        "Mitgliedern", "Mitarbeiter", "Mitarbeitende", "Menschen", "Personen", "Stück", "Teilnehmer", "Teilnehmende", "Schritte",
        "Kilometer", "Meter", "Tonnen", "Minuten", "Stunden", "Tag", "Tage", "Tagen", "Woche", "Wochen", "Monat", "Monate", "Monaten",
        "Jahr", "Jahre", "Jahren", "Wörter", "Zeichen", "Exemplare", "Besucher", "Haushalte", "Fälle", "Kunden", "Zuschauer",
        "Zuschauerinnen", "Fans", "Leser", "Nutzer", "Schüler", "Studierende", "Patienten", "Betten", "Wohnungen", "Plätze", "Kinder",
        "Stimmen", "Unterschriften", "Downloads", "Instanzen",
    };

    /// <summary>True when <paramref name="topic"/> holds an email address, a phone or account number, a date of birth or a street address it recognizes.</summary>
    public static bool HoldsContactDetails(this string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return false;
        return Email().IsMatch(topic)
            || Iban().Matches(topic).Any(m => m.Value.Count(char.IsDigit) >= 8)
            || LongNumber(topic)
            || Phone().Matches(topic).Any(m => m.Value.Count(char.IsDigit) >= MinNumberDigits)
            || Labelled().Matches(topic).Any(m => m.Groups["number"].Value.Count(char.IsDigit) >= MinLabelledDigits)
            || (BirthWord().IsMatch(topic) && (AnyDate().IsMatch(topic) || BirthYear().IsMatch(topic)))
            || Addresses(Street(), topic)
            || Addresses(ShortStreet(), topic)
            || EnglishStreet().IsMatch(topic)
            || Postcode().Matches(topic).Any(m => !CountWords.Contains(m.Groups["word"].Value));
    }

    private static bool Addresses(Regex pattern, string topic) =>
        pattern.Matches(topic).Any(m => !CountWords.Contains(m.Groups["after"].Value));

    private static bool LongNumber(string topic)
    {
        foreach (Match run in NumberRun().Matches(topic))
        {
            var groups = GroupSplit().Split(run.Value).Where(g => g.Length > 0).ToList();
            var digits = run.Value.Count(char.IsDigit);
            if (groups.Any(g => g.Length >= MinNumberDigits))
                return true;
            if (groups.Count >= 3 && digits >= MinGroupedDigits && !groups.All(IsYear))
                return true;
        }
        return false;
    }

    private static bool IsYear(string group) => group.Length == 4 && (group.StartsWith("19", StringComparison.Ordinal) || group.StartsWith("20", StringComparison.Ordinal));

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+\s*(?:@|\(at\)|\[at\]|\{at\})\s*[\p{L}\p{N}-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]){11,30}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Iban();

    [GeneratedRegex(@"\d[\d \t/\-]*\d", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRun();

    [GeneratedRegex(@"[ \t/\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex GroupSplit();

    [GeneratedRegex(@"(?<![\p{N}.,])(?:\+\s?\d|\(\s?0\d*\s?\)|0\d)[\d \t./()\-–—]*\d", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b(?:konto(?:nummer|nr\.?)?|kto\.?|kundennummer|kundennr\.?|vertragsnummer|versicherungsnummer|mitgliedsnummer|aktenzeichen|az\.|account(?: number| no\.?)?|acct\.?|customer number|policy number)\s*:?\s*(?<number>\d[\d .\-/]*\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Labelled();

    [GeneratedRegex(@"\b(?:geb\.|geboren|geburtsdatum|geburtstag|jahrgang|born|date of birth|dob|birthday|birth date)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BirthWord();

    [GeneratedRegex(@"\b\d{1,2}\s?[./]\s?\d{1,2}\s?[./]\s?(?:\d{2}|\d{4})\b|\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AnyDate();

    [GeneratedRegex(@"\b(?:19\d{2}|20[01]\d)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BirthYear();

    [GeneratedRegex(@"\p{L}{2,}(?:straße|strasse|str\.?|weg|gasse|allee|ring|damm|ufer|chaussee)\s*(?:(?:nr\.?|no\.?|hausnummer)\s*)?(?!(?:19|20)\d{2}(?!\p{N}))\d{1,4}\s?[a-z]?(?![\p{L}\p{N}])(?:\s+(?<after>\p{L}+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Street();

    [GeneratedRegex(@"\b(?:Am|An der|An den|Auf der|Auf dem|Unter den|Hinter der|Hinterm|Beim)\s+\p{Lu}[\p{L}-]+\s+(?!(?:19|20)\d{2}(?!\p{N}))\d{1,4}\s?[a-z]?(?![\p{L}\p{N}])(?:\s+(?<after>\p{L}+))?", RegexOptions.CultureInvariant)]
    private static partial Regex ShortStreet();

    [GeneratedRegex(@"\b(?!(?:19|20)\d{2}\b)\d{1,5}\s+(?:\p{Lu}[\p{L}-]*\s+){1,3}(?:Street|St\.?|Road|Rd\.?|Avenue|Ave\.?|Lane|Ln\.?|Drive|Boulevard|Blvd\.?|Court|Ct\.?)(?![\p{L}])", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishStreet();

    [GeneratedRegex(@"(?<!\b(?:ISO|DIN|EN|IEC|VDE|VDI|ÖNORM|BS|NF|SN|IEEE|RFC|EN ISO|DIN EN|DIN EN ISO)[ /]?)\b\d{5}\s+(?<word>\p{Lu}[\p{Ll}ß-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Postcode();
}
