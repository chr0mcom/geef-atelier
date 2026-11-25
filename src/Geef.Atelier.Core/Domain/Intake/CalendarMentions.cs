using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>
/// Finds calendar elements in a text without an AI model: dates written with digits (<c>12.5.</c>, <c>12.5.2027</c>, <c>2027-05-12</c>,
/// <c>05/12/2027</c>), month names next to a digit (<c>5. Mai</c>, <c>May 5</c>, <c>Mai 2027</c>; a month name alone never counts) and
/// weekday names as whole words. The names come from the calendars of German, English, French, Spanish, Italian and the language of the
/// order, never from a kept word list. Durations and amounts (<c>1 bis 3 Jahre</c>, <c>2 Seiten</c>) are no calendar elements.
/// </summary>
public static partial class CalendarMentions
{
    private static readonly string[] CalendarLanguages = ["de", "en", "fr", "es", "it"];

    private static readonly ConcurrentDictionary<string, CalendarNames> NamesByLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The calendar elements of <paramref name="text"/> in one form, so that the same date written in the request, a file or an answer
    /// option compares equal whatever the language or notation: a day of a month as <c>--MM-DD</c>, a month of a year as <c>YYYY-MM</c>
    /// (a full date gives both) and a weekday as its English name (<c>Saturday</c>). A full date also gives its weekday, so that an option
    /// naming the weekday of a date of the request counts as named there. <paramref name="language"/> is the language of the order and may
    /// be <c>null</c>.
    /// </summary>
    public static IReadOnlyList<string> Find(string text, string? language)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var names = NamesByLanguage.GetOrAdd(Key(language), CalendarNames.Build);
        var found = new List<string>();

        foreach (Match match in DottedDate().Matches(text))
            AddDate(found, Number(match, "day"), Number(match, "month"), Year(match));
        foreach (Match match in IsoDate().Matches(text))
            AddDate(found, Number(match, "day"), Number(match, "month"), Year(match));
        foreach (Match match in SlashDate().Matches(text))
        {
            var first = Number(match, "first");
            var second = Number(match, "second");
            var year = Year(match);
            AddDate(found, first, second, year);
            AddDate(found, second, first, year);
        }

        foreach (Match match in names.DayMonth.Matches(text))
            AddDate(found, Number(match, "day"), names.Month(match), Year(match));
        foreach (Match match in names.MonthDay.Matches(text))
            AddDate(found, Number(match, "day"), names.Month(match), Year(match));
        foreach (Match match in names.MonthYear.Matches(text))
            AddMonthOfYear(found, names.Month(match), Year(match));
        foreach (Match match in names.Weekday.Matches(text))
        {
            if (names.WeekdayOf(match) is { } day)
                Add(found, day.ToString());
        }

        return found;
    }

    private static string Key(string? language)
    {
        var key = (language ?? string.Empty).Trim().ToLowerInvariant();
        var dash = key.IndexOfAny(['-', '_']);
        return dash > 0 ? key[..dash] : key;
    }

    private static void AddDate(List<string> found, int day, int month, int? year)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31)
            return;
        Add(found, $"--{month:00}-{day:00}");
        if (year is not ({ } y and >= 1 and <= 9999))
            return;
        AddMonthOfYear(found, month, y);
        if (day <= DateTime.DaysInMonth(y, month))
            Add(found, new DateOnly(y, month, day).DayOfWeek.ToString());
    }

    private static void AddMonthOfYear(List<string> found, int month, int? year)
    {
        if (month is >= 1 and <= 12 && year is >= 1 and <= 9999)
            Add(found, $"{year:0000}-{month:00}");
    }

    private static void Add(List<string> found, string element)
    {
        if (!found.Contains(element, StringComparer.Ordinal))
            found.Add(element);
    }

    private static int Number(Match match, string group) =>
        match.Groups[group] is { Success: true } g && int.TryParse(g.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static int? Year(Match match)
    {
        var group = match.Groups["year"];
        if (!group.Success || !int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return null;
        return group.Length == 2 ? 2000 + year : year;
    }

    private static string Folded(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"(?<![\d.,])(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?:(?<year>\d{4}|\d{2})(?![\d.,]\d)(?!\d)|(?!\d))", RegexOptions.CultureInvariant)]
    private static partial Regex DottedDate();

    [GeneratedRegex(@"(?<![\d\-])(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<![\d/])(?<first>\d{1,2})/(?<second>\d{1,2})/(?<year>\d{4}|\d{2})(?![\d/])", RegexOptions.CultureInvariant)]
    private static partial Regex SlashDate();

    private sealed class CalendarNames(
        Dictionary<string, int> months,
        Dictionary<string, DayOfWeek> weekdays,
        Regex dayMonth,
        Regex monthDay,
        Regex monthYear,
        Regex weekday)
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public Regex DayMonth { get; } = dayMonth;

        public Regex MonthDay { get; } = monthDay;

        public Regex MonthYear { get; } = monthYear;

        public Regex Weekday { get; } = weekday;

        public int Month(Match match) => months.GetValueOrDefault(Folded(match.Groups["month"].Value));

        public DayOfWeek? WeekdayOf(Match match) =>
            weekdays.TryGetValue(Folded(match.Groups["weekday"].Value), out var day) ? day : null;

        public static CalendarNames Build(string language)
        {
            var months = new Dictionary<string, int>(StringComparer.Ordinal);
            var weekdays = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal);
            var monthSpellings = new HashSet<string>(StringComparer.Ordinal);
            var weekdaySpellings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var culture in Cultures(language))
            {
                var format = culture.DateTimeFormat;
                for (var i = 0; i < 12; i++)
                {
                    AddName(months, monthSpellings, format.MonthNames[i], i + 1);
                    AddName(months, monthSpellings, format.MonthGenitiveNames[i], i + 1);
                    if (format.AbbreviatedMonthNames[i].Trim().EndsWith('.'))
                        AddName(months, monthSpellings, format.AbbreviatedMonthNames[i], i + 1);
                }
                for (var i = 0; i < 7; i++)
                    AddName(weekdays, weekdaySpellings, format.DayNames[i], (DayOfWeek)i);
            }

            var month = Alternation(monthSpellings);
            return new CalendarNames(
                months,
                weekdays,
                new Regex($@"(?<![\p{{L}}\d])(?<day>\d{{1,2}})(?:\.|er|st|nd|rd|th|º|°)?\s*(?:de\s+|of\s+)?(?<month>{month})(?!\p{{L}})(?:\s*,?\s*(?:de\s+)?(?<year>\d{{4}})(?!\d))?", Options),
                new Regex($@"(?<!\p{{L}})(?<month>{month})\.?\s+(?<day>\d{{1,2}})(?:st|nd|rd|th)?(?!\d)(?:\s*,?\s*(?<year>\d{{4}})(?!\d))?", Options),
                new Regex($@"(?<!\p{{L}})(?<month>{month})\.?\s*,?\s*(?:de\s+|of\s+)?(?<year>\d{{4}})(?!\d)", Options),
                new Regex($@"(?<!\p{{L}})(?<weekday>{Alternation(weekdaySpellings)})s?(?!\p{{L}})", Options));
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

        private static void AddName<T>(Dictionary<string, T> names, HashSet<string> spellings, string name, T value)
        {
            var trimmed = name.Trim();
            var folded = Folded(trimmed);
            if (folded.Count(char.IsLetter) < 3)
                return;
            names.TryAdd(folded, value);
            spellings.Add(trimmed.Normalize(NormalizationForm.FormC));
            spellings.Add(folded);
        }

        private static string Alternation(IEnumerable<string> spellings) =>
            string.Join('|', spellings.OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).Select(Regex.Escape));
    }
}
