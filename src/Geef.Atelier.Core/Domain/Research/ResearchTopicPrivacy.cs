using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>
/// Recognizes, without an AI model, research topics that hold a contact or account detail of a person: an email address, a phone
/// number, an IBAN or another long number, a date of birth, a street with a house number or a postcode with a town. Such a topic is
/// withheld by <see cref="ResearchQueryGate"/>, whatever the request says.
/// </summary>
/// <remarks>
/// The rules are narrow on purpose, so that years, year ranges, paragraphs, amounts written with separators and the numbers of norms
/// still go out: a number counts only from seven digits in a row, from seven digits when it starts with <c>0</c> or <c>+</c> and is
/// split by spaces, slashes, hyphens or brackets, or from ten digits in at least three groups. A date counts as a date of birth with a
/// word for birth in the topic (also with a year from 1900 to 2019 alone), or when its year lies before 2000. A house number is never a
/// year from 1900 to 2099. A five-digit postcode counts only before a word in capitals that is
/// not a unit or a count, and never after the name of a norm. Names, health details and other personal details in plain words are
/// not recognized; the texts for the person say so.
/// </remarks>
public static partial class ResearchTopicPrivacy
{
    /// <summary>Fewest digits of a number that counts as a phone, account or customer number.</summary>
    public const int MinNumberDigits = 7;

    /// <summary>Fewest digits of a number in three or more groups that counts as an account number without a leading zero.</summary>
    public const int MinGroupedDigits = 10;

    private static readonly HashSet<string> CountWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Euro", "EUR", "Dollar", "USD", "CHF", "Franken", "Pfund", "GBP", "Einwohner", "Einwohnern", "Mitglieder", "Mitgliedern",
        "Mitarbeiter", "Mitarbeitende", "Menschen", "Personen", "Stück", "Teilnehmer", "Teilnehmende", "Schritte", "Kilometer", "Meter",
        "Tonnen", "Stunden", "Jahre", "Tage", "Wörter", "Zeichen", "Exemplare", "Besucher", "Haushalte", "Fälle", "Kunden",
    };

    /// <summary>True when <paramref name="topic"/> holds an email address, a phone or account number, a date of birth or a street address.</summary>
    public static bool HoldsContactDetails(this string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return false;
        return Email().IsMatch(topic)
            || Iban().Matches(topic).Any(m => m.Value.Count(char.IsDigit) >= 8)
            || LongNumber(topic)
            || BirthDate(topic)
            || Street().IsMatch(topic)
            || EnglishStreet().IsMatch(topic)
            || Postcode().Matches(topic).Any(m => !CountWords.Contains(m.Groups["word"].Value));
    }

    private static bool LongNumber(string topic)
    {
        foreach (Match run in NumberRun().Matches(topic))
        {
            var value = run.Value.Trim();
            var groups = GroupSplit().Split(value).Where(g => g.Length > 0).ToList();
            var digits = value.Count(char.IsDigit);
            if (groups.Any(g => g.Count(char.IsDigit) >= MinNumberDigits))
                return true;
            if (digits >= MinNumberDigits && (value[0] == '0' || value[0] == '+'))
                return true;
            if (groups.Count >= 3 && digits >= MinGroupedDigits)
                return true;
        }
        return false;
    }

    private static bool BirthDate(string topic) =>
        OldDate().IsMatch(topic) || (BirthWord().IsMatch(topic) && (AnyDate().IsMatch(topic) || Year().IsMatch(topic)));

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+\s*(?:@|\(at\)|\[at\]|\{at\})\s*[\p{L}\p{N}-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]){11,30}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Iban();

    [GeneratedRegex(@"\+?\(?\d[\d \t/()\-]*\d|\+\d", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRun();

    [GeneratedRegex(@"[ \t/()\-+]+", RegexOptions.CultureInvariant)]
    private static partial Regex GroupSplit();

    [GeneratedRegex(@"\b(?:geb\.|geboren|geburtsdatum|geburtstag|jahrgang|born|date of birth|dob|birthday|birth date)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BirthWord();

    [GeneratedRegex(@"\b\d{1,2}\s?[./]\s?\d{1,2}\s?[./]\s?(?:\d{2}|\d{4})\b|\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AnyDate();

    [GeneratedRegex(@"\b(?:19\d{2}|20[01]\d)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Year();

    [GeneratedRegex(@"\b\d{1,2}\s?[./]\s?\d{1,2}\s?[./]\s?1\d{3}\b|\b1\d{3}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex OldDate();

    [GeneratedRegex(@"[\p{L}-]*(?:straße|strasse|str\.|weg|gasse|allee|platz|damm|ufer|chaussee)\s*(?!(?:19|20)\d{2}(?!\p{N}))\d{1,4}\s?[a-z]?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Street();

    [GeneratedRegex(@"\b\d{1,5}\s+(?:\p{Lu}[\p{L}-]*\s+){1,3}(?:Street|St\.?|Road|Rd\.?|Avenue|Ave\.?|Lane|Ln\.?|Drive|Boulevard|Blvd\.?|Court|Ct\.?)(?![\p{L}])", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishStreet();

    [GeneratedRegex(@"(?<!\b(?:ISO|DIN|EN|IEC|VDE|VDI|ÖNORM|BS|NF|SN|IEEE|RFC|EN ISO|DIN EN|DIN EN ISO)[ /]?)\b\d{5}\s+(?<word>\p{Lu}[\p{Ll}ß-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Postcode();
}
