using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>
/// Tells without an AI model whether a text names a date. Two readings on purpose: the material of the person is read generously
/// (<see cref="AnyDateLike"/>), an answer option strictly (<see cref="NamesCalendarDay"/>). Single dates are never compared, so a date
/// that stands anywhere in the material keeps every option. The month and weekday names come from the calendars of German, English,
/// French, Spanish, Italian and the language of the order, never from a kept word list. All patterns run in linear time
/// (<see cref="RegexOptions.NonBacktracking"/>), also over whole file texts.
/// </summary>
public static class CalendarMentions
{
    private const string Day = "(?:0?[1-9]|[12][0-9]|3[01])";
    private const string Month = "(?:0?[1-9]|1[0-2])";
    private const string Year = "(?:[12][0-9]{3})";
    private const string Blank = "[ \u00a0]?";
    private const string Ordinal = @"(?:\.|st|nd|rd|th|er|e|º|°)?";

    private static readonly string[] CalendarLanguages = ["de", "en", "fr", "es", "it"];

    private static readonly ConcurrentDictionary<string, Patterns> PatternsByLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when any of <paramref name="texts"/> holds something that could be a date, read generously: digits as a day and month in
    /// any usual notation (<c>12.5.</c>, <c>12.05</c>, <c>12. 05. 2027</c>, <c>5/12</c>, <c>05/2027</c>, <c>2027-05-12</c>), a month name
    /// or abbreviation next to a digit (<c>5. Dez.</c>, <c>Sep 5</c>, <c>3 al 7 de mayo</c>), a month name alone in the spelling of its
    /// calendar (<c>Mai</c>, <c>December</c>, <c>mars</c>; the lower-case English words <c>may</c> and <c>march</c> do not count) and a
    /// weekday name at the start of a word (<c>Samstagnachmittag</c>, <c>samstags</c>, <c>Fridays</c>). In doubt it says yes.
    /// </summary>
    public static bool AnyDateLike(IEnumerable<string?> texts, string? language)
    {
        var patterns = For(language);
        return texts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => Nfc(text!))
            .Any(text => patterns.Material.IsMatch(text) || patterns.MonthAlone.IsMatch(text));
    }

    /// <summary>
    /// True when <paramref name="option"/> names a calendar day, read strictly: a date with digits and a closing point or year
    /// (<c>12.5.</c>, <c>12.05.2027</c>, <c>2027-05-12</c>, <c>05/12/2027</c>), a month name next to a digit (<c>5. Mai</c>,
    /// <c>May 5</c>, <c>Mai 2027</c>) or a weekday name as a whole word. Durations, amounts and lengths (<c>1 bis 3 Jahre</c>,
    /// <c>12,50 Euro</c>, <c>2 Seiten</c>), a month name alone and version numbers (<c>1.2.3</c>) do not count.
    /// </summary>
    public static bool NamesCalendarDay(string? option, string? language) =>
        !string.IsNullOrWhiteSpace(option) && For(language).Option.IsMatch(Nfc(option));

    private static string Nfc(string text) => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    private static Patterns For(string? language)
    {
        var key = (language ?? string.Empty).Trim().ToLowerInvariant();
        var dash = key.IndexOfAny(['-', '_']);
        return PatternsByLanguage.GetOrAdd(dash > 0 ? key[..dash] : key, Patterns.Build);
    }

    private sealed class Patterns(Regex material, Regex monthAlone, Regex option)
    {
        private const RegexOptions Linear = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

        public Regex Material { get; } = material;

        public Regex MonthAlone { get; } = monthAlone;

        public Regex Option { get; } = option;

        public static Patterns Build(string language)
        {
            var months = new HashSet<string>(StringComparer.Ordinal);
            var abbreviations = new HashSet<string>(StringComparer.Ordinal);
            var weekdays = new HashSet<string>(StringComparer.Ordinal);
            foreach (var format in Cultures(language).Select(c => c.DateTimeFormat))
            {
                foreach (var name in format.MonthNames.Concat(format.MonthGenitiveNames))
                    Add(months, name);
                foreach (var name in format.AbbreviatedMonthNames.Concat(format.AbbreviatedMonthGenitiveNames))
                    Add(abbreviations, name);
                foreach (var name in format.DayNames)
                    Add(weekdays, name);
            }

            var anyMonth = Alternation(months.Concat(abbreviations));
            var spelledMonth = Alternation(months.SelectMany(AsWritten));
            var spelledShortMonth = Alternation(abbreviations.SelectMany(AsWritten));
            var weekday = Alternation(weekdays);
            var material =
                $@"\b{Day}{Blank}[./]{Blank}{Month}\b|\b{Month}{Blank}[./-]{Blank}{Year}\b|\b{Year}{Blank}[-/.]{Blank}{Month}\b|\b{Day}-{Month}-[0-9]{{2,4}}\b" +
                $@"|[0-9]{Ordinal}\s?(?:de\s|of\s)?(?:{anyMonth})\b|\b(?:{anyMonth})\b\.?\s?[0-9]|\b(?:{weekday})";
            var option =
                $@"\b{Day}\.{Month}\.(?:[0-9]{{4}}|[0-9]{{2}})?(?:[^0-9]|$)|\b{Year}-{Month}-{Day}\b|\b[0-9]{{1,2}}/[0-9]{{1,2}}/(?:[0-9]{{4}}|[0-9]{{2}})\b" +
                $@"|\b[0-9]{{1,2}}{Ordinal}\s?(?:de\s|of\s)?(?-i:{spelledMonth}|(?:{spelledShortMonth})\.)(?:[^\p{{L}}]|$)" +
                $@"|(?-i:\b(?:{spelledMonth})\b|\b(?:{spelledShortMonth})\.)\s?[0-9]|\b(?:{weekday})s?\b";
            return new Patterns(
                new Regex(material, Linear | RegexOptions.IgnoreCase),
                new Regex($@"\b(?:{spelledMonth})\b", Linear),
                new Regex(option, Linear | RegexOptions.IgnoreCase));
        }

        private static IEnumerable<CultureInfo> Cultures(string language)
        {
            foreach (var name in CalendarLanguages.Append(language).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                CultureInfo? culture;
                try
                {
                    culture = CultureInfo.GetCultureInfo(name);
                }
                catch (CultureNotFoundException)
                {
                    culture = null;
                }
                if (culture is not null && !Equals(culture, CultureInfo.InvariantCulture))
                    yield return culture;
            }
        }

        private static void Add(HashSet<string> names, string name)
        {
            var trimmed = name.Trim().TrimEnd('.').Normalize(NormalizationForm.FormC);
            if (trimmed.Count(char.IsLetter) < 3)
                return;
            names.Add(trimmed);
            names.Add(WithoutAccents(trimmed));
        }

        private static IEnumerable<string> AsWritten(string name)
        {
            yield return name;
            if (char.IsLower(name[0]))
                yield return char.ToUpperInvariant(name[0]) + name[1..];
        }

        private static string WithoutAccents(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var c in name.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Alternation(IEnumerable<string> names) =>
            string.Join('|', names.Distinct(StringComparer.Ordinal).OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).Select(Regex.Escape));
    }
}
