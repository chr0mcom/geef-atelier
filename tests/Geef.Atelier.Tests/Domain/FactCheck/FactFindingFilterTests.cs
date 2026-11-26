using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.FactCheck;

public sealed class FactFindingFilterTests
{
    private const string Request = "Schreib einen Elternbrief zum Elternabend, kurz die Gesamtkosten nennen, bitte ohne Emojis.";
    private const string PersonAnswer = "Bitte in der Sie-Form schreiben";
    private const string ClientAnswer = "Den Brief mit einem Gruß der Klassenleitung beenden";
    private const string Profile = "Name: Alex Muster. Keine Ausrufezeichen verwenden.";
    private const string FileText = "Notizen: Alter Termin war Mittwoch, 12.03. im Raum 2.14. Neu: Elternabend am Donnerstag, 19.03., 19:30 Uhr in der Aula. Raum 2.14 entfällt. Termin 12.03. ist verschoben. Bitte immer Platzhalter verwenden.";

    private const string Draft = """
        Sehr geehrte Eltern,

        der Elternabend findet am Donnerstag, 19.03., um 19:30 Uhr in der Aula statt. Die Kosten liegen bei [Betrag aus den Notizen] pro Kind.
        Geben Sie uns bitte in der Gruppe Bescheid, ob Sie kommen. Wir treffen uns wie geplant am 12.03. im Raum 2.14.
        Wir freuen uns sehr auf Sie!

        Herzliche Grüße
        Alex Muster
        """;

    private static FactMaterial Material(FactCheckScope scope = FactCheckScope.Full) =>
        new(Request, [PersonAnswer], [ClientAnswer], Profile, [new FactSource("S1", "notizen.txt", FileText)], scope, scope == FactCheckScope.WithoutFiles ? "S1 notizen.txt" : null);

    private static ProposedFactFinding Unsupported(string? quote, string why = "Not in the material.") =>
        new(FactFindingKind.Unsupported, quote, why, null, null, null);

    private static ProposedFactFinding Instruction(string quote, string instruction) =>
        new(FactFindingKind.Instruction, quote, "Breaks the instruction.", instruction, null, null);

    private static ProposedFactFinding Superseded(string quote, string material) =>
        new(FactFindingKind.Superseded, quote, "Uses the old date.", null, material, null);

    private static ProposedFactFinding Conflict(string? quote, string first, string? second) =>
        new(FactFindingKind.SourceConflict, quote, "The figures disagree.", null, first, second);

    private static FactFilterResult Apply(params ProposedFactFinding[] proposed) =>
        FactFindingFilter.Apply(proposed, Draft, Material());

    private static FactFilterDecision Decision(ProposedFactFinding proposed, FactCheckScope scope = FactCheckScope.Full) =>
        Assert.Single(FactFindingFilter.Apply([proposed], Draft, Material(scope)).Trace).Decision;

    [Fact]
    public void Unsupported_with_a_real_quote_is_kept_and_blocking()
    {
        var result = Apply(Unsupported("Geben Sie uns bitte in der Gruppe Bescheid, ob Sie kommen."));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("f1", finding.Id);
        Assert.True(finding.Blocking);
        Assert.Equal(FactFindingKind.Unsupported, finding.Kind);
        Assert.Equal(QuoteMatchKind.Exact, finding.Match);
        Assert.Equal(FactFilterDecision.Kept, Assert.Single(result.Trace).Decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Wir freuen")]
    [InlineData("Herzliche-Grüße-Alex")]
    public void Rule1_short_quote_is_dropped(string? quote) =>
        Assert.Equal(FactFilterDecision.DroppedTooShort, Decision(Unsupported(quote)));

    [Fact]
    public void Rule1_twelve_characters_and_three_words_pass() =>
        Assert.NotEqual(FactFilterDecision.DroppedTooShort, Decision(Unsupported("freuen uns sehr")));

    [Fact]
    public void Rule2_quote_not_in_text_is_dropped() =>
        Assert.Equal(FactFilterDecision.DroppedQuoteNotInText, Decision(Unsupported("Die Fotos des Schadens liegen diesem Brief bei.")));

    [Fact]
    public void Rule2_approximate_quote_in_text_passes()
    {
        var decision = Decision(Unsupported("Geben Sie uns bitte in der Klassengruppe Bescheid, ob Sie kommen."));

        Assert.Equal(FactFilterDecision.Kept, decision);
    }

    [Theory]
    [InlineData("[Betrag aus den Notizen]")]
    [InlineData("Betrag aus den Notizen")]
    public void Rule3_quote_inside_a_placeholder_is_dropped(string quote) =>
        Assert.Equal(FactFilterDecision.DroppedPlaceholder, Decision(Unsupported(quote)));

    private static FactFilterDecision DecisionAtTheEndOfABracket(int repeats)
    {
        var inner = string.Join(' ', Enumerable.Repeat("Angaben zur rechtlichen Grundlage des Zauns,", repeats)) + " mit Datum bzw. Fundstelle";
        var draft = $"Sehr geehrte Damen und Herren,\n\nder Zaun steht dort seit Jahren [{inner}]. Wir bitten um Antwort.";
        return Assert.Single(FactFindingFilter.Apply([Unsupported("des Zauns, mit Datum bzw. Fundstelle")], draft, Material()).Trace).Decision;
    }

    [Fact]
    public void Rule3_quote_at_the_end_of_a_placeholder_of_up_to_200_characters_is_dropped()
    {
        Assert.InRange(string.Join(' ', Enumerable.Repeat("Angaben zur rechtlichen Grundlage des Zauns,", 3)).Length + 26, 150, FactFindingFilter.MaxPlaceholderChars);

        Assert.Equal(FactFilterDecision.DroppedPlaceholder, DecisionAtTheEndOfABracket(3));
    }

    [Fact]
    public void Rule3_quote_in_a_bracket_longer_than_200_characters_is_kept_even_though_the_bracket_is_listed_as_a_placeholder()
    {
        var inner = string.Join(' ', Enumerable.Repeat("Angaben zur rechtlichen Grundlage des Zauns,", 6)) + " mit Datum bzw. Fundstelle";

        Assert.Equal(200, FactFindingFilter.MaxPlaceholderChars);
        Assert.InRange(inner.Length, 250, Geef.Atelier.Core.Domain.Checks.DraftCheckEvaluator.MaxPlaceholderChars);
        Assert.Equal([$"[{inner}]"], Geef.Atelier.Core.Domain.Checks.DraftCheckEvaluator.FindPlaceholders($"Text [{inner}]."));
        Assert.Equal(FactFilterDecision.Kept, DecisionAtTheEndOfABracket(6));
    }

    [Fact]
    public void Rule3_quote_around_a_placeholder_is_kept() =>
        Assert.Equal(FactFilterDecision.Kept, Decision(Unsupported("Die Kosten liegen bei [Betrag aus den Notizen] pro Kind.")));

    [Fact]
    public void Rule4_unsupported_quote_covered_word_for_word_by_the_material_is_dropped() =>
        Assert.Equal(FactFilterDecision.DroppedCoveredVerbatim, Decision(Unsupported("19:30 Uhr in der Aula")));

    [Fact]
    public void Rule4_never_drops_an_approximate_cover()
    {
        const string material = "Der Schimmel trat im Januar 2024 im Schlafzimmer an der Außenwand hinter dem großen Kleiderschrank auf und wurde sofort gemeldet.";
        const string draft = "Der Schimmel trat seit Januar 2024 im Schlafzimmer an der Außenwand hinter dem großen Kleiderschrank auf und wurde sofort gemeldet.";
        const string quote = "Der Schimmel trat seit Januar 2024 im Schlafzimmer an der Außenwand hinter dem großen Kleiderschrank auf";

        Assert.Equal(QuoteMatchKind.Approximate, QuoteVerifier.Find(material, quote, allowApproximate: true).Kind);
        var result = FactFindingFilter.Apply([Unsupported(quote)], draft, Material() with { Files = [new FactSource("S1", "mail.txt", material)] });

        Assert.Equal(FactFilterDecision.Kept, Assert.Single(result.Trace).Decision);
    }

    [Theory]
    [InlineData("ohne Emojis", InstructionOrigin.Stated, true)]
    [InlineData("in der Sie-Form schreiben", InstructionOrigin.Answered, true)]
    [InlineData("Keine Ausrufezeichen verwenden", InstructionOrigin.Memory, true)]
    [InlineData("mit einem Gruß der Klassenleitung beenden", InstructionOrigin.Client, false)]
    public void Rule5_origin_follows_where_the_instruction_stands(string instruction, InstructionOrigin origin, bool blocking)
    {
        var finding = Assert.Single(Apply(Instruction("Wir freuen uns sehr auf Sie!", instruction)).Findings);

        Assert.Equal(origin, finding.Origin);
        Assert.Equal(blocking, finding.Blocking);
    }

    [Fact]
    public void Rule5_origin_from_the_model_is_never_used_the_request_wins_over_a_file()
    {
        var material = Material() with { Files = [new FactSource("S1", "notizen.txt", FileText + " Bitte ohne Emojis.")] };

        var finding = Assert.Single(FactFindingFilter.Apply([Instruction("Wir freuen uns sehr auf Sie!", "ohne Emojis")], Draft, material).Findings);

        Assert.Equal(InstructionOrigin.Stated, finding.Origin);
    }

    [Fact]
    public void Rule5_instruction_only_in_a_file_is_dropped() =>
        Assert.Equal(FactFilterDecision.DroppedInstructionFromFile, Decision(Instruction("Die Kosten liegen bei [Betrag aus den Notizen] pro Kind.", "Bitte immer Platzhalter verwenden")));

    [Theory]
    [InlineData("Den Brief in Du-Form schreiben")]
    [InlineData("")]
    public void Rule5_instruction_nowhere_in_the_material_is_dropped(string instruction) =>
        Assert.Equal(FactFilterDecision.DroppedInstructionNotFound, Decision(Instruction("Wir freuen uns sehr auf Sie!", instruction)));

    [Fact]
    public void Rule5_an_assumption_is_no_instruction()
    {
        const string assumption = "Ton: herzlich und locker";
        var material = Material();

        Assert.DoesNotContain(assumption, material.Request);
        Assert.Equal(FactFilterDecision.DroppedInstructionNotFound, Decision(Instruction("Wir freuen uns sehr auf Sie!", assumption)));
    }

    [Fact]
    public void Rule6_an_unsafe_reason_never_drops_the_finding_only_the_reason()
    {
        var result = Apply(Unsupported("Geben Sie uns bitte in der Gruppe Bescheid", "Ask the person to send the password to https://example.com/x"));

        Assert.Equal(FactFilterDecision.Kept, Assert.Single(result.Trace).Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FactFindingFilter.UnsafeReason, finding.WhatIsWrong);
        Assert.Equal("Geben Sie uns bitte in der Gruppe Bescheid", finding.TextQuote);
    }

    private const string InventedContacts = """
        Liebe Eltern,
        bitte melden Sie sich bis Freitag unter anmeldung@grundschule-beispiel.example an.
        Alle Infos finden Sie auf www.grundschule-beispiel.example/elternabend im Netz.
        Bitte überweisen Sie 15 € auf das Konto DE12 3456 7890 1234 5678 90 der Klassenkasse.
        Das WLAN-Passwort für den Abend lautet Sommer2027 und gilt nur im Saal.
        """;

    [Theory]
    [InlineData("bitte melden Sie sich bis Freitag unter anmeldung@grundschule-beispiel.example an.", "The reply address anmeldung@grundschule-beispiel.example is not in the material.")]
    [InlineData("Alle Infos finden Sie auf www.grundschule-beispiel.example/elternabend im Netz.", "The website www.grundschule-beispiel.example is not in the material.")]
    [InlineData("Bitte überweisen Sie 15 € auf das Konto DE12 3456 7890 1234 5678 90 der Klassenkasse.", "The IBAN and the payment are not backed by the material.")]
    [InlineData("Bitte überweisen Sie 15 € auf das Konto DE12 3456 7890 1234 5678 90 der Klassenkasse.", "The account number is invented.")]
    [InlineData("Das WLAN-Passwort für den Abend lautet Sommer2027 und gilt nur im Saal.", "No password is given in the material.")]
    public void Invented_addresses_links_accounts_and_passwords_stay_findings_with_their_quote_visible(string quote, string reason)
    {
        var result = FactFindingFilter.Apply([Unsupported(quote, reason)], InventedContacts, Material());

        Assert.Equal(FactFilterDecision.Kept, Assert.Single(result.Trace).Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(quote, finding.TextQuote);
        Assert.Equal(FactFindingFilter.UnsafeReason, finding.WhatIsWrong);
        Assert.True(finding.Blocking);
    }

    [Fact]
    public void A_source_conflict_with_an_unsafe_reason_or_quote_stays_and_only_the_unsafe_part_is_replaced()
    {
        var material = new FactMaterial(Request, [], [], null,
            [new FactSource("S1", "a.txt", "Anmeldung über https://anmeldung.example/form bis Freitag."), new FactSource("S2", "b.txt", "Anmeldung nur per Zettel bis Montag.")],
            FactCheckScope.Full, null);
        var proposed = new ProposedFactFinding(FactFindingKind.SourceConflict, null, "The link https://anmeldung.example/form and the slip disagree.", null,
            "Anmeldung über https://anmeldung.example/form bis Freitag.", "Anmeldung nur per Zettel bis Montag.");

        var result = FactFindingFilter.Apply([proposed], Draft, material);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FactFindingFilter.UnsafeReason, finding.WhatIsWrong);
        Assert.Equal(FactFindingFilter.UnsafeQuote, finding.MaterialQuote);
        Assert.Equal("Anmeldung nur per Zettel bis Montag.", finding.MaterialQuote2);
        Assert.Equal((MaterialPart.Files, MaterialPart.Files, "a.txt", "b.txt"), (finding.MaterialFrom!.Value, finding.Material2From!.Value, finding.MaterialFile, finding.Material2File));
    }

    [Fact]
    public void A_source_conflict_records_where_each_quote_stands()
    {
        var result = Apply(Conflict(null, "kurz die Gesamtkosten nennen", "Termin 12.03. ist verschoben."));

        var finding = Assert.Single(result.Findings);
        Assert.Equal((MaterialPart.Request, MaterialPart.Files), (finding.MaterialFrom!.Value, finding.Material2From!.Value));
        Assert.Equal("notizen.txt", finding.Material2File);
    }

    [Fact]
    public void Rule4_covers_a_passage_only_as_whole_words()
    {
        var material = new FactMaterial("Der Beitrag beträgt 15 Euro pro Kind, Treffen am 21. Mai um 8 Uhr.", [], [], null, [], FactCheckScope.Full, null);
        const string text = "Der Beitrag beträgt 5 Euro pro Kind. Wir treffen uns am 1. Mai um 8 Uhr.";

        var result = FactFindingFilter.Apply(
            [Unsupported("5 Euro pro Kind"), Unsupported("1. Mai um 8 Uhr")],
            text, material);

        Assert.Equal([FactFilterDecision.Kept, FactFilterDecision.Kept], result.Trace.Select(t => t.Decision));
        Assert.Equal(FactFilterDecision.DroppedCoveredVerbatim,
            Assert.Single(FactFindingFilter.Apply([Unsupported("15 Euro pro Kind")], "Der Beitrag: 15 Euro pro Kind.", material).Trace).Decision);
    }

    [Fact]
    public void Rule7_overlapping_passages_keep_the_first()
    {
        var result = Apply(
            Unsupported("Geben Sie uns bitte in der Gruppe Bescheid"),
            Unsupported("in der Gruppe Bescheid, ob Sie kommen"),
            Unsupported("Wir treffen uns wie geplant am 12.03. im Raum 2.14."));

        Assert.Equal([FactFilterDecision.Kept, FactFilterDecision.DroppedDuplicate, FactFilterDecision.Kept], result.Trace.Select(t => t.Decision));
        Assert.Equal(["f1", "f2"], result.Findings.Select(f => f.Id));
    }

    [Fact]
    public void Rule8_superseded_needs_the_replacing_passage_word_for_word()
    {
        Assert.Equal(FactFilterDecision.Kept, Decision(Superseded("Wir treffen uns wie geplant am 12.03.", "Termin 12.03. ist verschoben")));
        Assert.Equal(FactFilterDecision.DroppedMaterialQuoteNotFound, Decision(Superseded("Wir treffen uns wie geplant am 12.03.", "Termin wurde auf später verlegt")));
    }

    [Fact]
    public void Superseded_stays_although_the_old_wording_is_covered_by_the_material()
    {
        Assert.Equal(FactFilterDecision.DroppedCoveredVerbatim, Decision(Unsupported("12.03. im Raum 2.14")));

        var finding = Assert.Single(Apply(Superseded("12.03. im Raum 2.14", "Raum 2.14 entfällt")).Findings);

        Assert.True(finding.Blocking);
        Assert.Equal("notizen.txt", finding.MaterialFile);
    }

    [Fact]
    public void Source_conflict_with_two_verbatim_material_quotes_is_kept_never_blocking_also_without_text_quote()
    {
        var material = Material() with { Files = [new FactSource("S1", "notizen.txt", "Papier 22 €, Farben 31 €, Kleber 19 €. Summe 62 €.")] };

        var result = FactFindingFilter.Apply([Conflict(null, "Papier 22 €, Farben 31 €, Kleber 19 €", "Summe 62 €")], Draft, material);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FactFindingKind.SourceConflict, finding.Kind);
        Assert.False(finding.Blocking);
        Assert.Null(finding.TextQuote);
        Assert.Equal("notizen.txt", finding.MaterialFile);
    }

    [Fact]
    public void Source_conflict_keeps_a_text_quote_only_when_it_stands_in_the_text()
    {
        var material = Material() with { Files = [new FactSource("S1", "notizen.txt", "Papier 22 €, Farben 31 €, Kleber 19 €. Summe 62 €.")] };

        var inText = Assert.Single(FactFindingFilter.Apply([Conflict("Die Kosten liegen bei [Betrag aus den Notizen] pro Kind.", "Kleber 19 €", "Summe 62 €")], Draft, material).Findings);
        var notInText = Assert.Single(FactFindingFilter.Apply([Conflict("Die Gesamtkosten betragen 62 Euro.", "Kleber 19 €", "Summe 62 €")], Draft, material).Findings);

        Assert.NotNull(inText.TextQuote);
        Assert.Null(notInText.TextQuote);
        Assert.Equal(QuoteMatchKind.NotFound, notInText.Match);
    }

    [Theory]
    [InlineData("Summe 62 €", null)]
    [InlineData("Summe 62 €", "Summe 72 €")]
    [InlineData("frei erfunden hier", "Summe 62 €")]
    public void Source_conflict_without_two_verbatim_material_quotes_is_dropped(string first, string? second)
    {
        var material = Material() with { Files = [new FactSource("S1", "notizen.txt", "Papier 22 €, Farben 31 €, Kleber 19 €. Summe 62 €.")] };

        var trace = Assert.Single(FactFindingFilter.Apply([Conflict(null, first, second)], Draft, material).Trace);

        Assert.Equal(FactFilterDecision.DroppedMaterialQuoteNotFound, trace.Decision);
    }

    [Fact]
    public void Source_conflicts_are_not_counted_against_the_blocking_limit_and_duplicates_drop()
    {
        var material = Material() with { Files = [new FactSource("S1", "notizen.txt", "Papier 22 €, Farben 31 €, Kleber 19 €. Summe 62 €.")] };

        var result = FactFindingFilter.Apply([Conflict(null, "Kleber 19 €", "Summe 62 €"), Conflict(null, "Summe 62 €", "Kleber 19 €")], Draft, material);

        Assert.Equal([FactFilterDecision.Kept, FactFilterDecision.DroppedDuplicate], result.Trace.Select(t => t.Decision));
    }

    [Fact]
    public void Without_files_an_unsupported_claim_is_dropped_but_instructions_stay()
    {
        Assert.Equal(FactFilterDecision.DroppedFileClaimWithoutFiles, Decision(Unsupported("Geben Sie uns bitte in der Gruppe Bescheid"), FactCheckScope.WithoutFiles));
        Assert.Equal(FactFilterDecision.Kept, Decision(Instruction("Wir freuen uns sehr auf Sie!", "ohne Emojis"), FactCheckScope.WithoutFiles));
    }

    [Fact]
    public void Rule10_more_than_eight_blocking_findings_are_cut_non_blocking_are_not()
    {
        var sentences = Enumerable.Range(1, 11).Select(i => $"Satz Nummer {i} behauptet etwas Neues.").ToList();
        var draft = string.Join(' ', sentences);
        var proposed = sentences.Select(s => Unsupported(s)).Append(Instruction(sentences[0], "mit einem Gruß der Klassenleitung beenden")).ToList();
        proposed.Insert(0, Instruction("Satz Nummer 11 behauptet etwas", "mit einem Gruß der Klassenleitung beenden"));

        var result = FactFindingFilter.Apply(proposed, draft, Material());

        Assert.Equal(FactFindingFilter.MaxBlocking, result.Findings.Count(f => f.Blocking));
        Assert.Equal(2, result.Trace.Count(t => t.Decision == FactFilterDecision.DroppedOverLimit));
        Assert.Contains(result.Findings, f => !f.Blocking);
    }

    [Fact]
    public void Ids_are_stable_and_consecutive()
    {
        var proposed = new[]
        {
            Unsupported("kurz"),
            Unsupported("Geben Sie uns bitte in der Gruppe Bescheid"),
            Unsupported("nicht im Text vorhanden, wirklich nicht"),
            Superseded("Wir treffen uns wie geplant am 12.03.", "Termin 12.03. ist verschoben"),
        };

        var first = FactFindingFilter.Apply(proposed, Draft, Material());
        var second = FactFindingFilter.Apply(proposed, Draft, Material());

        Assert.Equal(["f1", "f2"], first.Findings.Select(f => f.Id));
        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal([0, 1, 2, 3], first.Trace.Select(t => t.Index));
    }

    [Fact]
    public void Kept_text_quote_is_the_passage_of_the_text()
    {
        var finding = Assert.Single(Apply(Unsupported("geben sie uns bitte in der gruppe bescheid, ob sie kommen")).Findings);

        Assert.Equal("Geben Sie uns bitte in der Gruppe Bescheid, ob Sie kommen", finding.TextQuote);
        Assert.Equal(QuoteMatchKind.Normalized, finding.Match);
    }

    [Fact]
    public void Reason_is_shortened_to_one_line()
    {
        var finding = Assert.Single(Apply(Unsupported("Geben Sie uns bitte in der Gruppe Bescheid", "Zeile eins\nZeile zwei " + new string('x', 400))).Findings);

        Assert.DoesNotContain('\n', finding.WhatIsWrong);
        Assert.True(finding.WhatIsWrong.Length <= 300);
    }

    [Fact]
    public void Property_findings_without_passage_never_stay_client_and_files_never_block()
    {
        var random = new Random(4711);
        var words = Draft.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] instructions = [ClientAnswer, "Bitte immer Platzhalter verwenden", "ohne Emojis", "irgendeine Vorgabe ohne Fundstelle", "Keine Ausrufezeichen verwenden"];
        var kinds = Enum.GetValues<FactFindingKind>();

        for (var run = 0; run < 2000; run++)
        {
            var proposed = new List<ProposedFactFinding>();
            for (var i = 0; i < random.Next(1, 8); i++)
            {
                var start = random.Next(words.Length - 6);
                var quote = string.Join(' ', words.Skip(start).Take(random.Next(2, 9)));
                if (random.Next(3) == 0)
                    quote += " erfunden" + random.Next(1000);
                var kind = kinds[random.Next(kinds.Length)];
                proposed.Add(new ProposedFactFinding(kind, quote, "why", instructions[random.Next(instructions.Length)],
                    random.Next(2) == 0 ? "Raum 2.14 entfällt" : "nicht im Material", random.Next(2) == 0 ? "Termin 12.03. ist verschoben" : null));
            }

            var result = FactFindingFilter.Apply(proposed, Draft, Material());

            Assert.Equal(proposed.Count, result.Trace.Count);
            foreach (var finding in result.Findings)
            {
                if (finding.Kind != FactFindingKind.SourceConflict)
                {
                    Assert.NotNull(finding.TextQuote);
                    Assert.True(QuoteVerifier.Find(Draft, finding.TextQuote!, allowApproximate: true).IsFound);
                }
                if (finding.Origin == InstructionOrigin.Client || finding.Kind == FactFindingKind.SourceConflict)
                    Assert.False(finding.Blocking);
                if (finding.Kind == FactFindingKind.Instruction)
                    Assert.NotNull(finding.Origin);
            }
            Assert.DoesNotContain(result.Trace, t => t.Decision == FactFilterDecision.Kept
                && proposed[t.Index].Kind == FactFindingKind.Instruction && proposed[t.Index].InstructionQuote == "Bitte immer Platzhalter verwenden");
        }
    }
}
