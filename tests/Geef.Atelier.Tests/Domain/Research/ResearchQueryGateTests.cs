using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Tests.Domain.Research;

public sealed class ResearchQueryGateTests
{
    private const string Request = "Schreib einen Aushang zum neuen Mindestlohn für unser Team.";

    private static QuerySelection Select(string topic, string request = Request, string? file = null, string? profile = null, int max = 4) =>
        ResearchQueryGate.Select([topic], request, file is null ? [] : [file], profile, max);

    private static void AssertSent(QuerySelection selection, string topic)
    {
        Assert.Equal([topic], selection.Sent);
        Assert.Equal(0, selection.Withheld);
    }

    private static void AssertWithheld(QuerySelection selection)
    {
        Assert.Empty(selection.Sent);
        Assert.Equal(1, selection.Withheld);
    }

    [Fact]
    public void Topic_without_a_word_of_the_files_is_sent() =>
        AssertSent(Select("Mindestlohn Deutschland 2026 Höhe", file: "Dienstplan Küche Spätschicht"), "Mindestlohn Deutschland 2026 Höhe");

    [Fact]
    public void Word_from_a_file_that_the_request_lacks_is_withheld() =>
        AssertWithheld(Select("Mindestlohn Bäckerei Sonnenschein", file: "Bäckerei Sonnenschein, Filiale Nord"));

    [Fact]
    public void The_same_word_in_the_request_lets_the_topic_go() =>
        AssertSent(Select("Mindestlohn Bäckerei", request: "Aushang zum Mindestlohn für unsere Bäckerei", file: "Bäckerei Sonnenschein"),
            "Mindestlohn Bäckerei");

    [Fact]
    public void A_name_from_the_profile_is_withheld() =>
        AssertWithheld(Select("Mindestlohn Hartmannsdorf", profile: "Name: Kim Beispiel, Ort: Hartmannsdorf"));

    [Fact]
    public void A_digit_sequence_from_a_file_is_withheld() =>
        AssertWithheld(Select("Tarifvertrag 48213 Mindestlohn", file: "Vertragsnummer 48213"));

    [Fact]
    public void A_short_number_from_a_file_is_a_word_like_any_other() =>
        AssertWithheld(Select("Mindestlohn 2026", file: "Planung für 2026"));

    [Theory]
    [InlineData("01701234567 Anbieter", "Telefon 0170 1234567")]
    [InlineData("Vertrag 0170-123 4567", "Telefon 01701234567")]
    [InlineData("Konto 370400440532", "IBAN DE89 3704 0044 0532 0130 00")]
    public void A_number_from_a_file_is_withheld_in_any_spelling(string topic, string file) =>
        AssertWithheld(Select(topic, file: file));

    [Fact]
    public void A_number_the_request_holds_is_sent() =>
        AssertSent(Select("ISO 27001 Anforderungen", request: "Merkblatt zu ISO 27001 für das Team", file: "Audit nach ISO 27001 im März"),
            "ISO 27001 Anforderungen");

    [Theory]
    [InlineData("Mindestlohn www.amt.example")]
    [InlineData("Mindestlohn https://amt.example/lohn")]
    [InlineData("Mindestlohn kontakt@amt.example")]
    [InlineData("IBAN Prüfziffer berechnen")]
    [InlineData("Passwort zurücksetzen Lohnportal")]
    public void Link_mail_and_account_details_are_withheld(string topic) =>
        AssertWithheld(Select(topic));

    [Fact]
    public void At_most_max_topics_go_and_the_rest_counts_over_the_limit()
    {
        var selection = ResearchQueryGate.Select(["Thema eins", "Thema zwei", "Thema drei", "Thema vier", "Thema fünf", "Thema sechs"], Request, [], null, 4);

        Assert.Equal(["Thema eins", "Thema zwei", "Thema drei", "Thema vier"], selection.Sent);
        Assert.Equal(2, selection.OverLimit);
        Assert.Equal(0, selection.Withheld);
    }

    [Fact]
    public void A_withheld_topic_takes_no_place_of_the_limit()
    {
        var selection = ResearchQueryGate.Select(["Filiale Nord Lohn", "Thema eins", "Thema zwei", "Thema drei"], Request, ["Filiale Nord"], null, 2);

        Assert.Equal(["Thema eins", "Thema zwei"], selection.Sent);
        Assert.Equal(1, selection.Withheld);
        Assert.Equal(1, selection.OverLimit);
    }

    [Theory]
    [InlineData("ＳＯＮＮＥＮＳＣＨＥＩＮ Lohn")]
    [InlineData("SONNENSCHEIN Lohn")]
    [InlineData("sonnenschein Lohn")]
    public void Width_and_case_do_not_hide_a_word(string topic) =>
        AssertWithheld(Select(topic, file: "Bäckerei Sonnenschein"));

    [Theory]
    [InlineData("Muller Lohn", "Kim Müller")]
    [InlineData("Mueller Lohn", "Kim Müller")]
    [InlineData("Müller Lohn", "Kim Mueller")]
    [InlineData("Strasse Lohn", "Straße 4")]
    [InlineData("Lindenstrasse Lohn", "Lindenstraße 4")]
    [InlineData("Jose Lohn", "José Beispiel")]
    public void Accents_and_umlauts_written_out_do_not_hide_a_word(string topic, string file) =>
        AssertWithheld(Select(topic, file: file));

    [Theory]
    [InlineData("Hartmann Lohn", "Praxis Hartmanns")]
    [InlineData("Hartmann Lohn", "Hartmannweg 3")]
    [InlineData("Hartmannweg Lohn", "Hartmann")]
    public void Inflected_and_compound_forms_of_a_file_word_are_withheld(string topic, string file) =>
        AssertWithheld(Select(topic, file: file));

    [Theory]
    [InlineData("Mindestlohn 2027", "Aushang zur Mindestlohnerhöhung", "Mindestlohn alt")]
    [InlineData("Mindestlohns Höhe", "Aushang zum Mindestlohn", "Tabelle des Mindestlohns")]
    [InlineData("Sonnenschein Lohn", "Aushang über Sonne im Sommer", "Bäckerei Sonnenschein")]
    [InlineData("Mueller Lohn", "Brief an Frau Müller", "Kim Müller")]
    public void Only_the_same_word_in_the_request_lets_a_file_word_go(string topic, string request, string file) =>
        AssertWithheld(Select(topic, request: request, file: file));

    [Fact]
    public void Known_gap_the_end_of_a_compound_word_is_not_compared() =>
        AssertSent(Select("Straße Lohn", file: "Lindenstraße 4"), "Straße Lohn");

    [Fact]
    public void Short_words_without_digits_do_not_count() =>
        AssertSent(Select("AG Gründung Kosten", file: "Beispiel AG"), "AG Gründung Kosten");

    [Fact]
    public void Blank_and_repeated_topics_are_skipped()
    {
        var selection = ResearchQueryGate.Select(["", "  ", "Mindestlohn  2026\n Höhe", "mindestlohn 2026 höhe"], Request, [], null, 4);

        Assert.Equal(["Mindestlohn 2026 Höhe"], selection.Sent);
        Assert.Equal(0, selection.Withheld);
        Assert.Equal(0, selection.OverLimit);
    }

    [Fact]
    public void Property_no_sent_topic_holds_a_word_of_files_or_profile_that_the_request_lacks()
    {
        string[] pool =
        [
            "mindestlohn", "Mindestlohn", "Bäckerei", "baeckerei", "Sonnenschein", "Filiale", "Nord", "Team", "Aushang", "Höhe", "Gesetz",
            "2026", "48213", "Hartmann", "Müller", "Mueller", "Straße", "Strasse", "Urlaub", "Feiertage", "England", "AG", "ab", "der", "die",
            "Lohn", "Löhne", "Stunde", "brutto", "netto", "Kim", "Café", "Cafe", "ÄRGER", "ärger", "Ｔｅａｍ", "Praxis", "Zahnarzt", "Ostern",
        ];
        var random = new Random(4711);
        var sentCount = 0;
        var withheldCount = 0;
        string Text(int words) => string.Join(random.Next(3) == 0 ? ", " : " ", Enumerable.Range(0, words).Select(_ => pool[random.Next(pool.Length)]));

        for (var run = 0; run < 5_000; run++)
        {
            var request = Text(random.Next(0, 8));
            var files = Enumerable.Range(0, random.Next(0, 3)).Select(_ => Text(random.Next(0, 12))).ToList();
            var profile = random.Next(2) == 0 ? null : Text(random.Next(0, 5));
            var topics = Enumerable.Range(0, random.Next(1, 6)).Select(_ => Text(random.Next(1, 5))).ToList();

            var selection = ResearchQueryGate.Select(topics, request, files, profile, 4);

            var foreign = ContractWords([.. files, profile ?? string.Empty]);
            foreign.ExceptWith(ContractWords([request]));
            foreach (var topic in selection.Sent)
                Assert.Empty(ContractWords([topic]).Intersect(foreign));
            Assert.True(selection.Sent.Count <= 4);
            sentCount += selection.Sent.Count;
            withheldCount += selection.Withheld;
        }

        Assert.True(sentCount > 2_000 && withheldCount > 2_000, $"sent {sentCount}, withheld {withheldCount}");
    }

    [Fact]
    public void Long_and_hostile_texts_finish_quickly()
    {
        var random = new Random(17);
        var builder = new StringBuilder();
        while (builder.Length < 4_000_000)
            builder.Append(CultureInfo.InvariantCulture, $"Wort{random.Next(200_000)} ").Append(random.Next(1_000_000)).Append(random.Next(3) == 0 ? "\n" : " - ");
        var huge = builder.ToString();
        var oneWord = new string('a', 2_000_000) + "ü" + new string('7', 500_000);
        var longTopic = string.Concat(Enumerable.Repeat("Mindestlohn 2026 ", 2_000));
        var topics = new[] { longTopic, "Mindestlohn Deutschland", "Feiertage England 2027", "Tarif " + new string('9', 5_000), "Urlaub Gesetz" };

        var stopwatch = Stopwatch.StartNew();
        var selection = ResearchQueryGate.Select(topics, Request + " " + huge[..100_000], [huge, oneWord], huge[..200_000], 4);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"took {stopwatch.Elapsed}");
        Assert.Equal(topics.Length, selection.Sent.Count + selection.Withheld + selection.OverLimit);
    }

    private static HashSet<string> ContractWords(IEnumerable<string> texts)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (Match match in Regex.Matches(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\p{L}\p{N}]+"))
            {
                if (match.Value.Length >= 3 || match.Value.Any(char.IsDigit))
                    words.Add(match.Value);
            }
        }
        return words;
    }
}
