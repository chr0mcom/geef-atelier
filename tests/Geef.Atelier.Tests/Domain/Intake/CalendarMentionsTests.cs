using System.Diagnostics;
using System.Text;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Two readings without a word list: the material of the person is read generously (<see cref="CalendarMentions.AnyDateLike"/>, in
/// doubt "a date"), an answer option strictly (<see cref="CalendarMentions.NamesCalendarDay"/>, no durations, amounts or lengths). Both
/// run in linear time, also over a million characters.
/// </summary>
public sealed class CalendarMentionsTests
{
    private static bool Material(string text, string? language = null) => CalendarMentions.AnyDateLike([text], language);

    [Theory]
    [InlineData("am 12.5.")]
    [InlineData("Anmeldung bis 12.05 oder 19.05 möglich")]
    [InlineData("Treffen 12.05, 18 Uhr")]
    [InlineData("am 12. 05. 2027")]
    [InlineData("Ferien 12.-16.05.")]
    [InlineData("on 2027-05-12")]
    [InlineData("on 05/12/2027")]
    [InlineData("on 5/12")]
    [InlineData("seit 05/2027")]
    [InlineData("am 12-05-2027")]
    [InlineData("Projektwoche vom 5. bis 9. Mai 2027")]
    [InlineData("Elternabend am 12. und 13. Mai")]
    [InlineData("am 5./6. Mai")]
    [InlineData("am 3. oder 4. Mai")]
    [InlineData("Open May 5-7, 2027")]
    [InlineData("May 5 and 6")]
    [InlineData("May 5-7 or May 12-14")]
    [InlineData("5-7 May")]
    [InlineData("12 and 19 May")]
    [InlineData("del 3 al 7 de mayo")]
    [InlineData("du 3 au 7 mai")]
    [InlineData("dal 3 al 7 maggio")]
    [InlineData("am 12. Dez.")]
    [InlineData("am 12. Dez")]
    [InlineData("on 5 Dec")]
    [InlineData("on Sep 5")]
    [InlineData("the 5th of May")]
    [InlineData("up to 3 may join")]
    [InlineData("im Mai")]
    [InlineData("Ende Dezember")]
    [InlineData("en mars")]
    [InlineData("in December")]
    [InlineData("Fr, 12. Juni")]
    [InlineData("am Sa, 6.6. oder So, 7.6.")]
    [InlineData("am Samstagnachmittag oder am Sonntagvormittag")]
    [InlineData("Freitagabend")]
    [InlineData("der Montagstermin")]
    [InlineData("samstags")]
    [InlineData("on Fridays")]
    [InlineData("le samedi")]
    [InlineData("el sábado")]
    [InlineData("lunedì")]
    [InlineData("lunedi")]
    public void The_material_counts_as_naming_a_date_in_every_usual_notation(string text) => Assert.True(Material(text), text);

    [Theory]
    [InlineData("Write a short email: our office is closed between Christmas and New Year 2026.")]
    [InlineData("You may choose the tone; we march on.")]
    [InlineData("Der Beitrag beträgt 12,50 Euro, Beginn 17 Uhr.")]
    [InlineData("Das Jahr 2027 wird gut.")]
    [InlineData("Seit 1 bis 3 Jahren, etwa 2 Seiten.")]
    [InlineData("")]
    public void Material_without_anything_date_like_counts_as_naming_none(string text) => Assert.False(Material(text), text);

    [Fact]
    public void Any_text_of_the_material_with_a_date_is_enough()
    {
        Assert.True(CalendarMentions.AnyDateLike(["Schreib einen Aushang.", null, "Notiz: Treffen am 12.05."], "de"));
        Assert.False(CalendarMentions.AnyDateLike(["Schreib einen Aushang.", null, "   "], "de"));
    }

    [Theory]
    [InlineData("12.5.")]
    [InlineData("12.05.2027")]
    [InlineData("12.5.27")]
    [InlineData("2027-05-12")]
    [InlineData("05/12/2027")]
    [InlineData("6/5/2027")]
    [InlineData("5. Mai")]
    [InlineData("Am 5. Mai 2027")]
    [InlineData("May 5")]
    [InlineData("May 5th, 2027")]
    [InlineData("Mai 2027")]
    [InlineData("le 1er janvier 2027")]
    [InlineData("el 5 de mayo de 2027")]
    [InlineData("il 5 maggio")]
    [InlineData("3. März")]
    [InlineData("le 3 févr. 2027")]
    [InlineData("12. Dez.")]
    [InlineData("Samstag")]
    [InlineData("samstags")]
    [InlineData("Saturday")]
    [InlineData("samedi")]
    [InlineData("sábado")]
    [InlineData("lunedì")]
    [InlineData("Monday, 4 January 2027")]
    public void An_option_names_a_calendar_day(string option) => Assert.True(CalendarMentions.NamesCalendarDay(option, null), option);

    [Theory]
    [InlineData("You may choose")]
    [InlineData("Up to 3 may join")]
    [InlineData("We march 5 km")]
    [InlineData("Im Mai")]
    [InlineData("Mitte Dezember")]
    [InlineData("1 bis 3 Jahre")]
    [InlineData("Entre 1 et 3 ans")]
    [InlineData("Kurz, etwa 1 Seite")]
    [InlineData("(1/2)")]
    [InlineData("Version 1.2.3")]
    [InlineData("12,50 Euro")]
    [InlineData("17 Uhr")]
    [InlineData("Das Jahr 2027")]
    [InlineData("Samstagnachmittag")]
    [InlineData("")]
    public void Durations_amounts_lengths_and_month_names_alone_name_no_day(string option) =>
        Assert.False(CalendarMentions.NamesCalendarDay(option, null), option);

    [Fact]
    public void The_order_language_adds_its_calendar_and_an_unknown_one_falls_back_to_the_five()
    {
        Assert.False(CalendarMentions.NamesCalendarDay("zaterdag", null));
        Assert.True(CalendarMentions.NamesCalendarDay("zaterdag", "nl"));
        Assert.True(CalendarMentions.AnyDateLike(["op 12 mei"], "nl-NL"));
        Assert.True(CalendarMentions.NamesCalendarDay("Samstag", "not-a-language"));
    }

    [Fact]
    public void Decomposed_umlauts_are_read_like_composed_ones()
    {
        var decomposed = "am 3. März".Normalize(NormalizationForm.FormD);
        Assert.NotEqual("am 3. März", decomposed);

        Assert.True(CalendarMentions.NamesCalendarDay(decomposed, "de"));
        Assert.True(CalendarMentions.AnyDateLike(["im März".Normalize(NormalizationForm.FormD)], "de"));
    }

    [Theory]
    [InlineData("Mai")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.")]
    [InlineData("Mai ")]
    [InlineData("9")]
    public void A_million_characters_take_linear_time(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 1_000_000 / unit.Length)) + "x";
        var watch = Stopwatch.StartNew();

        CalendarMentions.AnyDateLike([text], "de");
        CalendarMentions.NamesCalendarDay(text, "de");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"{watch.Elapsed} for {text.Length} characters of '{unit}'");
    }
}
