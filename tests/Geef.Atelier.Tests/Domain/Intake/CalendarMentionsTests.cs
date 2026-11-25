using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Calendar elements are found without a word list: digit dates, month names next to a digit and weekday names from the calendars of
/// the five languages and the order language, in one form for every notation.
/// </summary>
public sealed class CalendarMentionsTests
{
    [Theory]
    [InlineData("am 12.5.", "--05-12")]
    [InlineData("am 12.05.2027", "--05-12|2027-05|Wednesday")]
    [InlineData("am 12.5.27", "--05-12|2027-05|Wednesday")]
    [InlineData("on 2027-05-12", "--05-12|2027-05|Wednesday")]
    [InlineData("5. Mai", "--05-05")]
    [InlineData("am 5. Mai 2027", "--05-05|2027-05|Wednesday")]
    [InlineData("May 5", "--05-05")]
    [InlineData("May 5th, 2027", "--05-05|2027-05|Wednesday")]
    [InlineData("im Mai 2027", "2027-05")]
    [InlineData("le 1er janvier 2027", "--01-01|2027-01|Friday")]
    [InlineData("el 5 de mayo de 2027", "--05-05|2027-05|Wednesday")]
    [InlineData("il 5 maggio", "--05-05")]
    [InlineData("am 3. März", "--03-03")]
    [InlineData("am 3. MÄRZ", "--03-03")]
    [InlineData("le 3 févr. 2027", "--02-03|2027-02|Wednesday")]
    [InlineData("Samstag", "Saturday")]
    [InlineData("samstags", "Saturday")]
    [InlineData("Saturday", "Saturday")]
    [InlineData("samedi", "Saturday")]
    [InlineData("sábado", "Saturday")]
    [InlineData("sabato", "Saturday")]
    [InlineData("lunedì", "Monday")]
    [InlineData("Monday, 4 January 2027", "--01-04|2027-01|Monday")]
    public void A_calendar_element_is_found_in_one_form(string text, string expected) =>
        Assert.Equal(expected.Split('|').Order(StringComparer.Ordinal), CalendarMentions.Find(text, null).Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("You may choose")]
    [InlineData("Im Mai")]
    [InlineData("Entre 1 et 3 ans")]
    [InlineData("1 bis 3 Jahre")]
    [InlineData("Etwa eine halbe Seite (1/2)")]
    [InlineData("2 Seiten, rund 600 Wörter")]
    [InlineData("Version 1.2.3")]
    [InlineData("Preis 12,50 Euro")]
    [InlineData("17 Uhr")]
    [InlineData("In zwei Wochen")]
    [InlineData("Das Jahr 2027")]
    [InlineData("Sommerfest oder Gartenfest")]
    [InlineData("")]
    public void Durations_amounts_and_bare_words_are_no_calendar_elements(string text) =>
        Assert.Empty(CalendarMentions.Find(text, null));

    [Fact]
    public void A_slash_date_with_a_year_counts_in_both_readings()
    {
        var found = CalendarMentions.Find("05/12/2027", "en");

        Assert.Contains("--05-12", found);
        Assert.Contains("--12-05", found);
    }

    [Fact]
    public void The_order_language_adds_its_calendar()
    {
        Assert.DoesNotContain("Saturday", CalendarMentions.Find("zaterdag", null));
        Assert.Contains("Saturday", CalendarMentions.Find("zaterdag", "nl"));
        Assert.Contains("--05-12", CalendarMentions.Find("12 mei", "nl-NL"));
    }

    [Fact]
    public void An_unknown_language_falls_back_to_the_five_calendars()
    {
        Assert.Equal(["Saturday"], CalendarMentions.Find("Samstag", "not-a-language"));
    }
}
