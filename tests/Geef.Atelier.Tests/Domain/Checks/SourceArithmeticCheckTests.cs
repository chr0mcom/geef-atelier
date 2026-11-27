using System.Text;
using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

public sealed class SourceArithmeticCheckTests
{
    private const string ParentEveningNotes = """
        Notizen Elternabend 4b
        - Termin verschoben: nicht mehr Mittwoch 12.03., sondern Donnerstag 19.03., 19:30 Uhr
        - Raum 2.14 fällt weg, wir sind in der Aula
        - Zahlung bis 12.03. an die Klassenkasse
        - Material: Bastelpapier 22 €, Farben 31 €, Kleber 19 € – zusammen 62 €
        - Beitrag je Kind neu: 4,50 € (18 Kinder)
        - intern: Herrn X nicht auf das Thema ansprechen
        - Allergiehinweis: keine Nüsse beim Buffet, Tel. Schulbüro 0711 123456
        """;

    private static SourceArithmeticResult Check(string text) => SourceArithmeticCheck.Check([("S1", "notizen.txt", text)]);

    private static SourceArithmeticIssue Single(string text) => Assert.Single(Check(text).Issues);

    private static void Silent(string text) => Assert.Empty(Check(text).Issues);

    [Fact]
    public void Written_calculation_with_a_wrong_result_is_found()
    {
        var issue = Single("Material: 22 + 31 + 19 = 62");

        Assert.Equal(ArithmeticForm.Expression, issue.Form);
        Assert.Equal(["22 + 31 + 19"], issue.Items);
        Assert.Equal("62", issue.Stated);
        Assert.Equal("72", issue.Computed);
        Assert.Equal("22 + 31 + 19 = 62", issue.Quote);
        Assert.Equal("S1", issue.SourceId);
        Assert.Equal("notizen.txt", issue.FileName);
        Assert.Equal(TotalUseInText.Neither, issue.UseInText);
    }

    [Theory]
    [InlineData("3 × 24 € = 72 €")]
    [InlineData("3 x 24 € = 72 €")]
    [InlineData("3x24 = 72")]
    [InlineData("3 * 24 = 72")]
    [InlineData("3 · 24 = 72")]
    [InlineData("100 € - 20 € = 80 €")]
    [InlineData("100 − 20 = 80")]
    [InlineData("100 – 20 = 80")]
    [InlineData("2 × 15 + 10 = 40")]
    [InlineData("10 + 2 × 15 = 40")]
    [InlineData("22+31+19=72")]
    [InlineData("CHF 1'200.00 + CHF 300.00 = CHF 1'500.00")]
    [InlineData("1,25 × 1,25 = 1,56")]
    public void Correct_written_calculations_are_silent_but_counted(string text)
    {
        var result = Check(text);

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.TotalsFound);
    }

    [Theory]
    [InlineData("3 × 24 € = 62 €", "72 €")]
    [InlineData("3x24 = 70", "72")]
    [InlineData("100 € - 20 € = 70 €", "80 €")]
    [InlineData("€ 10 + € 5 = € 16", "€ 15")]
    [InlineData("2,50 € × 3 = 8,00 €", "7,50 €")]
    public void Wrong_written_calculations_give_the_computed_figure_in_the_spelling_of_the_file(string text, string computed) =>
        Assert.Equal(computed, Single(text).Computed);

    [Fact]
    public void List_with_a_total_line_is_recalculated()
    {
        var issue = Single("Material:\n- Bastelpapier 22 €\n- Farben 31 €\n- Kleber 19 €\nSumme: 62 €\n");

        Assert.Equal(ArithmeticForm.ListTotal, issue.Form);
        Assert.Equal(["22 €", "31 €", "19 €"], issue.Items);
        Assert.Equal("62 €", issue.Stated);
        Assert.Equal("72 €", issue.Computed);
        Assert.Equal("Summe: 62 €", issue.Quote);
    }

    [Theory]
    [InlineData("Wir haben Papier für 22 €, Farben für 31 € und Kleber für 19 € gekauft, zusammen 62 €.", "72 €")]
    [InlineData("We paid $22, $31 and $19, $62 in total.", "$72")]
    [InlineData("Kosten: Busfahrt 120 Euro, Eintritt 80 Euro, insgesamt 210 Euro.", "200 Euro")]
    public void Sentence_with_items_and_a_total_is_recalculated(string text, string computed)
    {
        var issue = Single(text);

        Assert.Equal(ArithmeticForm.SentenceTotal, issue.Form);
        Assert.Equal(computed, issue.Computed);
    }

    [Fact]
    public void Markdown_table_with_a_total_row_is_recalculated()
    {
        const string table = """
            | Posten | Menge | Betrag |
            |---|---:|---:|
            | Papier | 1 | 22,00 € |
            | Farben | 2 | 31,00 € |
            | Kleber |  | 19,00 € |
            | **Summe** | 3 | 62,00 € |
            """;

        var issue = Single(table);

        Assert.Equal(ArithmeticForm.TableTotal, issue.Form);
        Assert.Equal("62,00 €", issue.Stated);
        Assert.Equal("72,00 €", issue.Computed);
        Assert.Equal(2, Check(table).TotalsFound);
    }

    [Fact]
    public void Subtotal_rows_in_a_table_start_a_new_group()
    {
        const string table = """
            | Posten | Betrag |
            |---|---|
            | A | 100 € |
            | B | 50 € |
            | Zwischensumme | 140 € |
            | Versand | 10 € |
            | Gesamt | 160 € |
            """;

        var issue = Single(table);

        Assert.Equal("140 €", issue.Stated);
        Assert.Equal("150 €", issue.Computed);
    }

    [Fact]
    public void German_and_English_number_spelling()
    {
        Assert.Empty(Check("Miete 1.234,50 €\nNebenkosten 265,50 €\nGesamt 1.500,00 €").Issues);
        Assert.Equal("$1,500.00", Single("Rent $1,234.50\nUtilities $265.50\nTotal $1,600.00").Computed);
        Assert.Equal("2.000 €", Single("Miete 1.200 €\nNebenkosten 800 €\nGesamt 2.100 €").Computed);
        Assert.Equal("72,-", Single("Papier 22,-\nFarben 31,-\nKleber 19,-\nSumme 62,-").Computed);
    }

    [Fact]
    public void Negative_items_and_minus_signs()
    {
        Silent("Preis 50 €\nKorrektur -5 €\nSumme 45 €");
        Assert.Equal("40 €", Single("Preis 50 €\nKorrektur −10 €\nSumme 45 €").Computed);
    }

    [Fact]
    public void Parent_evening_notes_give_exactly_one_issue()
    {
        var result = Check(ParentEveningNotes);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("62 €", issue.Stated);
        Assert.Equal("72 €", issue.Computed);
        Assert.Equal(["22 €", "31 €", "19 €"], issue.Items);
        Assert.Equal(1, result.TotalsFound);
    }

    [Fact]
    public void Parent_evening_notes_as_a_list_give_exactly_one_issue()
    {
        const string notes = """
            Elternabend Klasse 3a, Donnerstag 19.03., 19:30 Uhr, Aula (nicht Raum 2.14)

            Material
            Bastelpapier: 22 €
            Farben: 31 €
            Kleber: 19 €
            Gesamtkosten: 62 €

            Beitrag je Kind: 4 € bei 18 Kindern
            """;

        var issue = Assert.Single(Check(notes).Issues);

        Assert.Equal(ArithmeticForm.ListTotal, issue.Form);
        Assert.Equal("72 €", issue.Computed);
    }

    [Theory]
    [InlineData("Die Materialkosten betragen insgesamt 72 Euro.", TotalUseInText.Computed)]
    [InlineData("Die Materialkosten betragen insgesamt 72,00 €.", TotalUseInText.Computed)]
    [InlineData("Die Materialkosten betragen insgesamt 62 €.", TotalUseInText.Stated)]
    [InlineData("Die Materialkosten werden noch mitgeteilt.", TotalUseInText.Neither)]
    [InlineData("Wir sind 72 Kinder und zahlen 62 Dollar.", TotalUseInText.Neither)]
    public void With_text_use_tells_which_figure_the_text_uses(string draft, TotalUseInText expected)
    {
        var result = SourceArithmeticCheck.WithTextUse(Check(ParentEveningNotes), draft);

        Assert.Equal(expected, Assert.Single(result.Issues).UseInText);
        Assert.Equal(1, result.TotalsFound);
    }

    [Theory]
    [InlineData("Am 12.03.2024 um 19:30 Uhr treffen wir uns zusammen mit 3 Kollegen.")]
    [InlineData("Treffen 12.03.\nTreffen 19.03.\nInsgesamt 2 Termine")]
    [InlineData("Beginn 18:00\nEnde 20:00\nGesamt 3 Stunden")]
    [InlineData("Telefon 0711 123456\nFax 0711 123457\nZusammen erreichbar unter 0711 1234")]
    [InlineData("Hauptstraße 12\nNebenstraße 14a\nzusammen 30 Wohnungen")]
    [InlineData("Wir kommen zusammen mit den Kindern.")]
    [InlineData("Preise: Kaffee 2,50 €, Tee 2,00 €, Kuchen 3,00 €.")]
    [InlineData("Papier 22 €\nFarben 31 $\nSumme 50 €")]
    [InlineData("Anteil A 40 %\nAnteil B 50 %\nGesamt 100 %")]
    [InlineData("Öffnungszeiten 9-17 = 8 Stunden")]
    [InlineData("Seite 3 + Anhang 2 = 7")]
    [InlineData("(22 + 31) × 2 = 106")]
    [InlineData("22 + 31 = 50 + 3")]
    [InlineData("Unser Motto: 1 + 1 = 3, denn gemeinsam schaffen wir mehr.")]
    [InlineData("Papier 3 Stück\nFarben 2 Stück\nGesamt 25 €")]
    [InlineData("Im Jahr hatten wir 12 Treffen und 4 Ausflüge, insgesamt 30 Teilnehmer.")]
    [InlineData("IBAN DE89 3704 0044 0532 0130 00, zusammen 12 €")]
    [InlineData("Die gesamte Klasse (24 Kinder) und 3 Lehrer kommen, 20 Plätze.")]
    [InlineData("Version 2.1 und 2.2, zusammen 4.3")]
    [InlineData("Gegründet 1998\nUmzug 2005\nGesamt 12")]
    [InlineData("Treffen am 12.03\nAusflug am 19.04\nzusammen 2")]
    [InlineData("Zahlung bis 12.03. an die Klassenkasse, insgesamt 18 Kinder.")]
    public void Counterexamples_are_silent(string text) => Silent(text);

    [Fact]
    public void Rounded_total_is_silent()
    {
        Silent("A 21,50 €\nB 40,40 €\nSumme ca. 62 €");
    }

    [Fact]
    public void A_total_of_the_last_items_is_correct()
    {
        Silent("Budget 200 €\nPapier 22 €\nFarben 31 €\nKleber 19 €\nSumme 72 €");
    }

    [Fact]
    public void Ambiguous_decimal_separator_without_evidence_is_silent()
    {
        Silent("Teil A 1.234\nTeil B 2.000\nSumme 3.500");
    }

    [Fact]
    public void Several_sources_keep_their_ids()
    {
        var result = SourceArithmeticCheck.Check([
            ("S0", "Ausgangstext", "Gesamt: 10 + 5 = 16"),
            ("S1", "a.txt", "3 × 4 = 12"),
            ("S2", "b.txt", "nichts zu rechnen"),
        ]);

        Assert.Equal(2, result.TotalsFound);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("S0", issue.SourceId);
        Assert.Equal("Ausgangstext", issue.FileName);
    }

    [Fact]
    public void Each_total_is_counted_once()
    {
        var result = Check("Papier 22 € + Farben 31 € = 53 €, zusammen 53 €");

        Assert.True(result.TotalsFound <= 2);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Check_expressions_in_a_text()
    {
        Assert.Empty(SourceArithmeticCheck.CheckExpressions("Drei Pakete: 3 × 24 € = 72 €."));
        var issue = Assert.Single(SourceArithmeticCheck.CheckExpressions("Die Posten 22 + 31 + 19 = 62 ergeben die Summe."));
        Assert.Equal("72", issue.Computed);
        Assert.Empty(SourceArithmeticCheck.CheckExpressions("Kein einziger Rechenweg, nur 22 Kinder und 31 Eltern, zusammen 62."));
    }

    [Fact]
    public void Draft_check_blocks_a_wrong_written_calculation()
    {
        var result = Assert.Single(DraftCheckEvaluator.Evaluate("Die Kosten: 22 + 31 + 19 = 62 Euro.", OrderConstraints.None),
            r => r.Code == DraftCheckCodes.Arithmetic);

        Assert.False(result.Passed);
        Assert.True(result.Blocking);
        Assert.Contains("22 + 31 + 19 = 72", result.Message);
        Assert.Equal("22 + 31 + 19 = 62 Euro", result.Quote);
    }

    [Fact]
    public void Draft_check_passes_a_correct_calculation_and_says_nothing_without_one()
    {
        var passed = Assert.Single(DraftCheckEvaluator.Evaluate("Drei Pakete: 3 × 24 € = 72 €.", OrderConstraints.None),
            r => r.Code == DraftCheckCodes.Arithmetic);
        Assert.True(passed.Passed);
        Assert.Equal(DraftCheckEvaluator.NoWrongCalculationFound, passed.Message);
        Assert.DoesNotContain("add up", passed.Message, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate("Die Gesamtkosten betragen 72 €.", OrderConstraints.None),
            r => r.Code == DraftCheckCodes.Arithmetic);
    }

    [Fact]
    public void Wrong_list_totals_in_a_text_are_no_draft_check()
    {
        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate("Papier 22 €\nFarben 31 €\nKleber 19 €\nSumme: 62 €", OrderConstraints.None),
            r => r.Code == DraftCheckCodes.Arithmetic);
    }

    /// <summary>
    /// Long runs of digits, separators, sums and totals must not cost more than about their length. Compared by growth
    /// (<see cref="ScalingAssert"/>), because a fixed limit in seconds failed on a loaded machine.
    /// </summary>
    [Fact]
    public void Long_and_hostile_inputs_stay_fast() => ScalingAssert.GrowsAboutLinearly(percent =>
    {
        var text = HostileArithmetic(percent);
        return () => SourceArithmeticCheck.WithTextUse(Check(text), text);
    });

    private static string HostileArithmetic(int percent)
    {
        var builder = new StringBuilder();
        builder.Append(string.Concat(Enumerable.Repeat("1.", 1_000 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("1,", 1_000 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("1 ", 1_000 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("123.456.789", 200 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("22 € + ", 200 * percent))).Append("= 1 €");
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("Posten 5 €. ", 200 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("zusammen 5 €, ", 200 * percent)));
        builder.Append('\n').Append(string.Concat(Enumerable.Repeat("Posten 3 €\n", 200 * percent))).Append("Summe 7 €");
        for (var i = 0; i < 200 * percent; i++)
            builder.Append('\n').Append(i % 7 == 0 ? $"Summe {i} €" : $"Posten {i % 100} €, zusammen {i} €, gesamt");
        return builder.ToString();
    }

    [Theory]
    [InlineData("Leistung 2.000,00 €\nabzgl. Anzahlung 500,00 €\nZahlbetrag gesamt 1.500,00 €")]
    [InlineData("Brutto 1.000,00 €\nabzüglich Skonto 20,00 €\nGesamtbetrag 980,00 €")]
    [InlineData("Bus 300 €\nEintritt 150 €\nGesamtkosten pro Kind: 18 €")]
    [InlineData("Einnahmen 900 €\nAusgaben 400 €\nGesamtsaldo 500 €")]
    [InlineData("| Posten | Betrag |\n|---|---|\n| Einnahmen | 900 € |\n| Ausgaben | 400 € |\n| Saldo gesamt | 500 € |")]
    [InlineData("Stühle: 4 Stück zu 25 €\nTische: 1 Stück zu 90 €\nGesamt: 190 €")]
    [InlineData("Artikel A 50 €\nArtikel B 69 €\ndavon MwSt 19 €\nGesamt 119 €")]
    [InlineData("Wir zahlen 300 € für den Bus und 150 € Eintritt, abzüglich 50 € Zuschuss, insgesamt 400 €.")]
    [InlineData("Room $300\nCatering $500\nless deposit $200\nTotal due $600")]
    public void Deductions_amounts_per_person_balances_taxes_and_quantities_stay_silent_and_are_not_counted(string text)
    {
        var result = Check(text);

        Assert.Empty(result.Issues);
        Assert.Equal(0, result.TotalsFound);
    }

    [Fact]
    public void A_total_without_unit_never_belongs_to_items_with_a_currency()
    {
        Silent("Bastelpapier 5 €\nFarben 12 €\nInsgesamt kommen 25 Kinder");
        Silent("Bastelpapier 5 €\nFarben 12 €\nSumme 25");
    }

    [Fact]
    public void A_total_that_only_matches_its_last_items_is_neither_reported_nor_counted_as_checked()
    {
        var result = Check("Pinsel 22 €\nPapier 31 €\nFarben 19 €\nSumme: 50 €");

        Assert.Empty(result.Issues);
        Assert.Equal(0, result.TotalsFound);
    }

    [Fact]
    public void A_numbered_list_still_counts_its_items()
    {
        var issue = Single("1. Pinsel 22 €\n2. Papier 31 €\n3. Farben 19 €\nSumme: 62 €");

        Assert.Equal("72 €", issue.Computed);
    }

    [Theory]
    [InlineData("300 € Bus + Eintritt 150 € + 20 € = 470 €")]
    [InlineData("Miete 500 € plus 100 € + 50 € = 650 €")]
    [InlineData("Bus 300 €, Eintritt 150 € + 20 € = 470 €")]
    public void A_written_calculation_with_more_figures_before_it_is_not_judged_and_never_blocks(string text)
    {
        Assert.Empty(SourceArithmeticCheck.CheckExpressions(text));
        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate(text, OrderConstraints.None), c => c.Code == DraftCheckCodes.Arithmetic);
    }

    [Theory]
    [InlineData("Bei 20 Kindern sind das 20 × 3 € = 70 €.", "60 €")]
    [InlineData("Wir erwarten 30 Gäste. 3 × 24 € = 70 €.", "72 €")]
    [InlineData("Für 25 Gäste: 22 € + 31 € + 19 € = 62 €.", "72 €")]
    [InlineData("For 25 guests: $22 + $31 + $19 = $62.", "$72")]
    public void A_bare_number_or_an_earlier_sentence_before_a_wrong_calculation_never_hides_it(string line, string computed)
    {
        var text = "2 × 10 € = 20 €\n" + line;

        var issue = Assert.Single(SourceArithmeticCheck.CheckExpressions(text));
        var check = Assert.Single(DraftCheckEvaluator.Evaluate(text, OrderConstraints.None), c => c.Code == DraftCheckCodes.Arithmetic);

        Assert.Equal(computed, issue.Computed);
        Assert.Equal((DraftCheckCodes.Arithmetic, false, true), (check.Code, check.Passed, check.Blocking));
    }

    [Theory]
    [InlineData("Der Beitrag: 2 € + 3 € = 5 €.\nMiete 500 € plus 100 € + 50 € = 700 €")]
    [InlineData("2 × 10 € = 20 €\nBus 300 €, Eintritt 150 € + 20 € = 999 €")]
    [InlineData("2 × 10 € = 20 €\nMiete 500 € zzgl. 100 € + 50 € = 999 €")]
    [InlineData("2 × 10 € = 20 €\nMiete 500 € zum 1. 100 € + 50 € = 999 €")]
    [InlineData("12 € + 13 € = 25 €\nDas sind 25 Personen × 12 € = 400 €.")]
    [InlineData("12 € + 13 € = 25 €\nRechnung: 25 Kinder x 12 € = 400 €.")]
    [InlineData("12 € + 13 € = 25 €\nDas macht 3 Tische à 15 € = 60 €.")]
    [InlineData("12 € + 13 € = 25 €\nZusammen: 50 € plus 20 € = 80 €.")]
    [InlineData("12 € + 13 € = 25 €\nDas sind 3 mal 24 € = 70 €.")]
    [InlineData("12 € + 13 € = 25 €\nEintritt 12 € und Material 15 € = 30 €.")]
    [InlineData("12 € + 13 € = 25 €\n3 Karten zu je 14 € = 52 €.")]
    [InlineData("$12 + $13 = $25\nThat is 25 guests × $12 = $400.")]
    [InlineData("$12 + $13 = $25\nTotal: $50 plus $20 = $80.")]
    public void A_calculation_that_was_left_out_is_never_reported_as_calculations_that_add_up(string text)
    {
        var check = Assert.Single(DraftCheckEvaluator.Evaluate(text, OrderConstraints.None),
            c => c.Code == DraftCheckCodes.Arithmetic);

        Assert.Equal((DraftCheckCodes.Arithmetic, true), (check.Code, check.Passed));
        Assert.Equal(DraftCheckEvaluator.NoWrongCalculationFound, check.Message);
        Assert.DoesNotContain("add up", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_text_whose_calculations_were_all_recalculated_still_never_says_they_add_up()
    {
        var check = Assert.Single(DraftCheckEvaluator.Evaluate("2 × 10 € = 20 €\nWir erwarten 30 Gäste. 3 × 24 € = 72 €.", OrderConstraints.None),
            c => c.Code == DraftCheckCodes.Arithmetic);

        Assert.Equal((DraftCheckCodes.Arithmetic, true, DraftCheckEvaluator.NoWrongCalculationFound), (check.Code, check.Passed, check.Message));
    }

    [Theory]
    [InlineData("Raum 200 € / Essen 150 € / Gesamtbudget: 500 €")]
    [InlineData("Room £200 / Food £150 / Total budget: £500")]
    [InlineData("Raum 200 €\nEssen 150 €\nGesamtbudget: 500 €")]
    [InlineData("Bastelpapier 5 €\nFarben 10 €\nGesamt für 20 Kinder: 300 €")]
    [InlineData("Bastelpapier 5 €, Farben 10 €, gesamt für 20 Kinder 300 €.")]
    [InlineData("Bus 300 €\nEintritt 150 €\nBereits gezahlt insgesamt: 300 €")]
    [InlineData("Beitrag 100 € / Frühbucherrabatt 20 € / Gesamt: 80 €")]
    [InlineData("Beitrag 100 €\nFrühbucherrabatt 20 €\nGesamt: 80 €")]
    [InlineData("Kosten 500 €\nZuschüsse 200 €\nGesamt 300 €")]
    [InlineData("Rechnung 500 €\nAnzahlungen 200 €\nGesamt 300 €")]
    [InlineData("Gläser 22 €, Kerzen 31 €, Deko 19 €, Summe inkl. Pfand 62 €.")]
    [InlineData("Gläser 22 € pro Stück, Kerzen 31 €, Deko 19 €, zusammen 62 €.")]
    [InlineData("Gläser 22 €, Kerzen 31 €, Deko 19 €, zusammen 62 €, davon sind noch 20 € offen.")]
    public void Budgets_payments_made_counts_and_deductions_in_compounds_or_plural_stay_silent(string text)
    {
        var result = Check(text);

        Assert.Empty(result.Issues);
        Assert.Equal(0, result.TotalsFound);
    }

    [Theory]
    [InlineData("Gläser 22 €, Kerzen 31 €, Deko 19 €, zusammen 62 €, also rund 3 € pro Familie.")]
    [InlineData("Gläser 22 €, Kerzen 31 €, Deko 19 €, zusammen 62 €, also 31 € je Tisch.")]
    [InlineData("Gläser 22 €, Kerzen 31 €, Deko 19 €, zusammen 62 €, also jeweils 31 € für zwei Tische.")]
    [InlineData("Gläser 22 €\nKerzen 31 €\nDeko 19 €\nzusammen 62 €, also rund 3 € pro Familie")]
    public void The_parents_evening_total_is_reported_although_a_later_clause_speaks_of_shares(string text)
    {
        var issue = Single(text);

        Assert.Equal(("62 €", "72 €"), (issue.Stated, issue.Computed));
    }

    [Theory]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 315 € (10 % Rabatt bereits abgezogen)")]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 300 € (nach Abzug des Zuschusses)")]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 300 €, abzüglich 50 € Zuschuss")]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 300 € – der Rabatt ist schon berücksichtigt")]
    [InlineData("Raum 100 € und Essen 50 €, zusammen 130 €, weil der Zuschuss von 20 € schon abgezogen ist.")]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 416,50 € (inkl. 19 % MwSt.)")]
    [InlineData("Raum 200 € / Essen 150 € / Gesamt: 416,50 €, inklusive Mehrwertsteuer")]
    [InlineData("Raum 200 €\nEssen 150 €\nGesamt: 300 € (nach Abzug des Zuschusses)")]
    [InlineData("Raum 200 €\nEssen 150 €\nGesamt: 416,50 €, inklusive Mehrwertsteuer")]
    [InlineData("Raum 200 €\nEssen 150 €\nGesamt: 280 €, Frühbucherrabatte berücksichtigt")]
    [InlineData("The room is $200 and the food $150, total $315 (10% discount applied).")]
    [InlineData("Room $200 / Food $150 / Total: $300 (after the early bird discount)")]
    [InlineData("Room $200 / Food $150 / Total: $400, including tax")]
    [InlineData("Room $200\nFood $150\nTotal: $400, incl. taxes")]
    public void Deductions_discounts_subsidies_and_taxes_in_a_later_clause_after_the_total_stay_silent(string text)
    {
        var result = Check(text);

        Assert.Empty(result.Issues);
        Assert.Equal(0, result.TotalsFound);
    }

    [Fact]
    public void The_parents_evening_total_in_english_is_reported_although_a_later_clause_speaks_of_each_family()
    {
        var issue = Single("Glasses $22, candles $31, decorations $19, in total $62, so about $3 for each family.");

        Assert.Equal(("$62", "$72"), (issue.Stated, issue.Computed));
    }

    [Fact]
    public void Items_described_as_paid_still_have_their_total_checked()
    {
        var issue = Single("Für den Ausflug haben wir 215 € für den Bus und 96 € für den Eintritt bezahlt, insgesamt also 300 €.");

        Assert.Equal("311 €", issue.Computed);
    }

    [Fact]
    public void A_clearly_wrong_written_calculation_in_the_text_still_blocks()
    {
        var check = Assert.Single(DraftCheckEvaluator.Evaluate("Material: 22 € + 31 € + 19 € = 62 €", OrderConstraints.None),
            c => c.Code == DraftCheckCodes.Arithmetic);

        Assert.False(check.Passed);
        Assert.True(check.Blocking);
    }

    [Fact]
    public void At_most_ten_wrong_totals_are_listed_and_the_rest_are_counted()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 15).Select(i => $"A {i} €\nB 2 €\nSumme {i + 10} €"));

        var result = Check(text);

        Assert.Equal(SourceArithmeticCheck.MaxListedIssues, result.Issues.Count);
        Assert.Equal(5, result.IssuesNotListed);
        Assert.Equal(15, result.TotalsFound);
    }

    [Fact]
    public void A_text_with_many_wrong_calculations_names_only_a_few_in_its_message()
    {
        var text = string.Join("\n", Enumerable.Range(1, 6).Select(i => $"{i + 10} € + 2 € = {i + 20} €"));

        var check = Assert.Single(DraftCheckEvaluator.Evaluate(text, OrderConstraints.None), c => c.Code == DraftCheckCodes.Arithmetic);

        Assert.Contains("and 3 more", check.Message);
    }
}
