using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.Writing;

public sealed class QuoteVerifierTests
{
    [Theory]
    [InlineData("Die Ver-\nsicherung zahlt", "Die Versicherung zahlt")]
    [InlineData("Die Ver-  \r\n   sicherung zahlt", "Die Versicherung zahlt")]
    [InlineData("Die Ver­sicherung zahlt", "Die Versicherung zahlt")]
    [InlineData("Die Ver­\nsicherung zahlt", "Die Versicherung zahlt")]
    [InlineData("Haus-\nGarten", "Haus- Garten")]
    [InlineData("„Gut“ und »besser« und ‚klein‘ und ‹eng›", "\"Gut\" und \"besser\" und 'klein' und 'eng'")]
    [InlineData("A – B — C ‐ D ‑ E ‒ F ― G", "A - B - C - D - E - F - G")]
    [InlineData("Warte…", "Warte...")]
    [InlineData("ﬁnal ① Ｔｅｓｔ", "final 1 Test")]
    [InlineData("  viel \t\n  Leerraum   hier  ", "viel Leerraum hier")]
    [InlineData("Null​Breite", "NullBreite")]
    [InlineData("Groß und klein", "Groß und klein")]
    public void Normalize_applies_each_rule(string input, string expected) =>
        Assert.Equal(expected, QuoteVerifier.Normalize(input));

    [Fact]
    public void Normalize_joins_decomposed_umlauts() =>
        Assert.Equal("Müller", QuoteVerifier.Normalize("Müller"));

    [Fact]
    public void Exact_quote_is_found_as_exact()
    {
        var match = QuoteVerifier.Find("Wir liefern am Montag die bestellten Teile.", "am Montag die bestellten Teile", false);

        Assert.Equal(QuoteMatchKind.Exact, match.Kind);
        Assert.True(match.IsVerbatim);
    }

    [Theory]
    [InlineData("Die Ver-\nsicherung zahlt den Schaden.", "Die Versicherung zahlt den Schaden")]
    [InlineData("Er sagte „wir kommen morgen“ am Telefon.", "Er sagte \"wir kommen morgen\" am Telefon")]
    [InlineData("Kosten 2020–2024 wurden geprüft", "Kosten 2020-2024 wurden geprüft")]
    [InlineData("Der Termin\n   ist am Freitag.", "Der Termin ist am Freitag")]
    [InlineData("Der Termin ist am Freitag.", "der termin ist am freitag")]
    public void Normalized_quote_is_found_as_normalized(string haystack, string quote)
    {
        var match = QuoteVerifier.Find(haystack, quote, false);

        Assert.Equal(QuoteMatchKind.Normalized, match.Kind);
        Assert.True(match.IsVerbatim);
    }

    [Fact]
    public void Case_difference_is_only_normalized_never_exact()
    {
        var match = QuoteVerifier.Find("Bitte Antworten Sie bis Freitag.", "bitte antworten sie bis freitag", false);

        Assert.Equal(QuoteMatchKind.Normalized, match.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData("elf Zeichen")]
    public void Quotes_under_twelve_characters_are_too_short(string quote) =>
        Assert.Equal(QuoteMatchKind.TooShort, QuoteVerifier.Find("elf Zeichen und noch viel mehr Text", quote, true).Kind);

    [Fact]
    public void Approximate_needs_the_flag()
    {
        const string haystack = "Der Vermieter hat die Heizung im Januar 2024 nach drei Wochen endlich repariert und die Rechnung bezahlt.";
        const string quote = "Der Vermieter hat die Heizung im Februar 2024 nach drei Wochen endlich repariert und die Rechnung bezahlt";

        Assert.Equal(QuoteMatchKind.NotFound, QuoteVerifier.Find(haystack, quote, false).Kind);
        var match = QuoteVerifier.Find(haystack, quote, true);
        Assert.Equal(QuoteMatchKind.Approximate, match.Kind);
        Assert.False(match.IsVerbatim);
        Assert.True(match.IsFound);
    }

    [Fact]
    public void Approximate_never_below_five_words()
    {
        const string haystack = "Lieferung kam völlig beschädigt an, leider.";

        Assert.Equal(QuoteMatchKind.NotFound, QuoteVerifier.Find(haystack, "Lieferung kam total beschädigt", true).Kind);
    }

    [Fact]
    public void Approximate_threshold_is_inclusive_at_0_88()
    {
        var words = Enumerable.Range(1, 25).Select(i => $"wort{i}").ToArray();
        var haystack = "Anfang " + string.Join(' ', words) + " Ende";
        var three = (string[])words.Clone();
        three[3] = "anders1";
        three[10] = "anders2";
        three[20] = "anders3";
        var four = (string[])three.Clone();
        four[15] = "anders4";

        var atThreshold = QuoteVerifier.Find(haystack, string.Join(' ', three), true);
        Assert.Equal(QuoteMatchKind.Approximate, atThreshold.Kind);
        Assert.Equal(0.88, atThreshold.Similarity, 3);
        Assert.Equal(QuoteMatchKind.NotFound, QuoteVerifier.Find(haystack, string.Join(' ', four), true).Kind);
    }

    [Fact]
    public void Counterexample_since_january_ongoing_is_never_verbatim()
    {
        const string material = "Der Schimmel trat im Januar 2024 im Schlafzimmer auf.";

        foreach (var allow in new[] { false, true })
        {
            var match = QuoteVerifier.Find(material, "seit Januar 2024 andauernd", allow);
            Assert.False(match.IsVerbatim);
        }
    }

    [Fact]
    public void Start_and_length_refer_to_the_normalized_text()
    {
        const string haystack = "Erste   Zeile.\n\nDie Ver-\nsicherung zahlt den Schaden.";
        var match = QuoteVerifier.Find(haystack, "Die Versicherung zahlt den Schaden", false);
        var normalized = QuoteVerifier.Normalize(haystack);

        Assert.Equal("Die Versicherung zahlt den Schaden", normalized.Substring(match.Start, match.Length));
    }

    [Fact]
    public void Exact_match_also_points_into_the_normalized_text()
    {
        const string haystack = "Vorher   viel   Raum. Hier steht der Satz.";
        var match = QuoteVerifier.Find(haystack, "Hier steht der Satz", false);

        Assert.Equal(QuoteMatchKind.Exact, match.Kind);
        Assert.Equal("Hier steht der Satz", QuoteVerifier.Normalize(haystack).Substring(match.Start, match.Length));
    }

    [Fact]
    public void Excerpt_returns_the_original_passage()
    {
        const string haystack = "Vorher.\nEr schrieb „wir kommen\n  morgen“ am Telefon.";
        var match = QuoteVerifier.Find(haystack, "Er schrieb \"wir kommen morgen\" am Telefon", false);

        Assert.Equal("Er schrieb „wir kommen\n  morgen“ am Telefon", QuoteVerifier.Excerpt(haystack, match));
    }

    [Fact]
    public void Excerpt_of_an_approximate_match_is_the_passage_of_the_text()
    {
        const string haystack = "Einleitung. Der Vermieter hat die Heizung im Januar 2024 nach drei Wochen endlich repariert und die Rechnung bezahlt. Schluss.";
        var match = QuoteVerifier.Find(haystack, "Der Vermieter hat die Heizung im Februar 2024 nach drei Wochen endlich repariert und die Rechnung bezahlt", true);

        Assert.Equal("Der Vermieter hat die Heizung im Januar 2024 nach drei Wochen endlich repariert und die Rechnung bezahlt.", QuoteVerifier.Excerpt(haystack, match));
    }

    [Fact]
    public void Find_verbatim_accepts_shorter_quotes_but_never_approximates()
    {
        Assert.Equal(QuoteMatchKind.Exact, QuoteVerifier.FindVerbatim("Summe: 62 €", "62 €", 4).Kind);
        Assert.Equal(QuoteMatchKind.TooShort, QuoteVerifier.FindVerbatim("Summe: 62 €", "62", 4).Kind);
        Assert.Equal(QuoteMatchKind.NotFound, QuoteVerifier.FindVerbatim("Summe: 62 €", "63 €", 4).Kind);
    }

    /// <summary>Fifty quotes in a long text; compared by growth (<see cref="ScalingAssert"/>) instead of a limit in seconds.</summary>
    [Fact]
    public void Fifty_quotes_in_six_hundred_thousand_characters_cost_about_the_length_of_the_text() => ScalingAssert.GrowsAboutLinearly(percent =>
    {
        var random = new Random(7);
        var vocabulary = Enumerable.Range(0, 3000).Select(i => $"w{i:x}{(char)('a' + i % 26)}").ToArray();
        var builder = new System.Text.StringBuilder();
        while (builder.Length < 6_000 * percent)
        {
            builder.Append(vocabulary[random.Next(vocabulary.Length)]);
            builder.Append(random.Next(12) == 0 ? ".\n" : " ");
        }
        var haystack = builder.ToString();
        var quotes = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            var start = random.Next(haystack.Length - 400);
            var space = haystack.IndexOf(' ', start) + 1;
            var quote = haystack.Substring(space, 120);
            quotes.Add(i % 3 == 0 ? quote.Replace(" w1", " zz", StringComparison.Ordinal) : i % 3 == 1 ? quote.ToUpperInvariant() : quote);
        }

        return () =>
        {
            foreach (var quote in quotes)
                QuoteVerifier.Find(haystack, quote, true);
        };
    });

    [Fact]
    public void Common_words_only_do_not_explode() => ScalingAssert.GrowsAboutLinearly(percent =>
    {
        var haystack = string.Concat(Enumerable.Repeat("der die das und ist ein ", 250 * percent));
        return () => Assert.True(QuoteVerifier.Find(haystack, "der die das und war ein der die das und ist ein", true).IsFound);
    });

    [Theory]
    [InlineData("Der Beitrag: 15 Euro pro Kind.", "5 Euro pro Kind", false)]
    [InlineData("Treffen am 21. Mai um 8 Uhr.", "1. Mai um 8 Uhr", false)]
    [InlineData("Der Beitrag: 5 Euro pro Kind.", "5 Euro pro Kind", true)]
    [InlineData("Der Beitrag: 15 Euro pro Kind und 5 Euro pro Kind.", "5 Euro pro Kind", true)]
    [InlineData("Treffen „am Montag“ – in der Aula", "Treffen \"am Montag\" - in der", true)]
    [InlineData("Treffen am MONTAG in der Aula", "treffen am montag", true)]
    [InlineData("Der Termin", "", false)]
    public void Whole_words_are_required_at_both_ends(string haystack, string quote, bool expected) =>
        Assert.Equal(expected, QuoteVerifier.ContainsWholeWords(haystack, quote));

    [Theory]
    [InlineData("Der Beitrag ist bis zum 21. März, zu zahlen.", "1. März, zu zahlen.", "21. März, zu zahlen.")]
    [InlineData("Er kostet 125 Euro pro Kind und Tag und wird bar bezahlt.", "25 Euro pro Kind und Tag und wird bar bezahlt", "125 Euro pro Kind und Tag und wird bar bezahlt")]
    [InlineData("Wir treffen uns im Gemeindehaus am Markt.", "reffen uns im Gemeindehaus am Mar", "treffen uns im Gemeindehaus am Markt")]
    [InlineData("Wir treffen uns im Gemeindehaus am Markt.", "Wir treffen uns im Gemeindehaus", "Wir treffen uns im Gemeindehaus")]
    public void An_excerpt_that_starts_or_ends_inside_a_word_is_widened_to_whole_words_and_found_again(string haystack, string quote, string expected)
    {
        var excerpt = QuoteVerifier.ExcerptWholeWords(haystack, QuoteVerifier.Find(haystack, quote, allowApproximate: true));

        Assert.Equal(expected, excerpt);
        Assert.True(QuoteVerifier.ContainsWholeWords(haystack, excerpt));
        Assert.Equal(expected == quote, QuoteVerifier.ContainsWholeWords(haystack, quote));
    }
}
