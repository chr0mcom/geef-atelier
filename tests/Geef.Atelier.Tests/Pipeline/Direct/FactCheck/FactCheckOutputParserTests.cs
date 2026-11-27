using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

public sealed class FactCheckOutputParserTests
{
    internal const string Valid = """
        {
          "findings": [
            { "kind": "unsupported", "text_quote": "Fotos des Schadens liegen diesem Schreiben bei.", "what_is_wrong": "The request does not mention photos.",
              "instruction_quote": null, "material_quote": null, "material_quote_2": null },
            { "kind": "instruction", "text_quote": "Mit freundlichen Grüßen und bis bald", "what_is_wrong": "The person asked for no closing phrase.",
              "instruction_quote": "ohne Grußformel", "material_quote": null, "material_quote_2": null },
            { "kind": "superseded", "text_quote": "Wir treffen uns im Raum 104 der Schule.", "what_is_wrong": "The room was moved.",
              "instruction_quote": null, "material_quote": "Raum 104 fällt aus, wir sind in der Aula.", "material_quote_2": null },
            { "kind": "source_conflict", "text_quote": null, "what_is_wrong": "The total does not add up.",
              "instruction_quote": null, "material_quote": "Bastelmaterial 22 Euro", "material_quote_2": "Summe 62 Euro" }
          ]
        }
        """;

    internal const string Empty = """{"findings":[]}""";

    private static string Finding(string kind = "unsupported", string? textQuote = "Der Termin ist am Montag um zehn Uhr.", string whatIsWrong = "Not backed.",
        string? instruction = null, string? material = null, string? material2 = null) =>
        new JsonObject
        {
            ["kind"] = kind,
            ["text_quote"] = textQuote,
            ["what_is_wrong"] = whatIsWrong,
            ["instruction_quote"] = instruction,
            ["material_quote"] = material,
            ["material_quote_2"] = material2,
        }.ToJsonString();

    private static string Findings(params string[] findings) => $$"""{"findings":[{{string.Join(',', findings)}}]}""";

    [Fact]
    public void Reads_all_four_kinds_with_their_quotes()
    {
        var findings = FactCheckOutputParser.ParseCheck(Valid)!;

        Assert.Equal([FactFindingKind.Unsupported, FactFindingKind.Instruction, FactFindingKind.Superseded, FactFindingKind.SourceConflict],
            findings.Select(f => f.Kind));
        Assert.Equal(new ProposedFactFinding(FactFindingKind.Unsupported, "Fotos des Schadens liegen diesem Schreiben bei.",
            "The request does not mention photos.", null, null, null), findings[0]);
        Assert.Equal("ohne Grußformel", findings[1].InstructionQuote);
        Assert.Equal("Raum 104 fällt aus, wir sind in der Aula.", findings[2].MaterialQuote);
        Assert.Null(findings[3].TextQuote);
        Assert.Equal(("Bastelmaterial 22 Euro", "Summe 62 Euro"), (findings[3].MaterialQuote, findings[3].MaterialQuote2));
    }

    [Fact]
    public void An_empty_list_is_a_valid_answer_without_findings()
    {
        var findings = FactCheckOutputParser.ParseCheck(Empty);

        Assert.NotNull(findings);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I cannot help with that.")]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"findings":{}}""")]
    [InlineData("""{"findings":[ broken""")]
    [InlineData("""[{"kind":"unsupported"}]""")]
    public void An_unreadable_answer_is_null(string? json) => Assert.Null(FactCheckOutputParser.ParseCheck(json));

    [Fact]
    public void Text_around_the_json_object_is_tolerated()
    {
        var findings = FactCheckOutputParser.ParseCheck("```json\n" + Findings(Finding()) + "\n```");

        Assert.Single(findings!);
    }

    [Theory]
    [InlineData("invented")]
    [InlineData("style")]
    [InlineData("")]
    public void A_finding_with_an_unknown_kind_is_dropped(string kind)
    {
        var findings = FactCheckOutputParser.ParseCheck(Findings(Finding(kind), Finding()))!;

        Assert.Equal(FactFindingKind.Unsupported, Assert.Single(findings).Kind);
    }

    [Fact]
    public void Kinds_are_read_without_regard_to_case_or_spaces()
    {
        var findings = FactCheckOutputParser.ParseCheck(Findings(Finding(" Source_Conflict ", null, material: "a", material2: "b")))!;

        Assert.Equal(FactFindingKind.SourceConflict, Assert.Single(findings).Kind);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("instruction")]
    [InlineData("material")]
    [InlineData("material2")]
    public void A_quote_over_600_characters_drops_the_finding(string field)
    {
        var tooLong = new string('x', FactCheckOutputParser.MaxQuoteChars + 1);
        var finding = field switch
        {
            "text" => Finding(textQuote: tooLong),
            "instruction" => Finding("instruction", instruction: tooLong),
            "material" => Finding("superseded", material: tooLong),
            _ => Finding("source_conflict", null, material: "Summe 62 Euro", material2: tooLong),
        };

        var findings = FactCheckOutputParser.ParseCheck(Findings(finding, Finding()))!;

        Assert.Equal("Der Termin ist am Montag um zehn Uhr.", Assert.Single(findings).TextQuote);
    }

    [Fact]
    public void A_quote_of_exactly_600_characters_is_kept_unchanged()
    {
        var quote = new string('x', FactCheckOutputParser.MaxQuoteChars);

        var findings = FactCheckOutputParser.ParseCheck(Findings(Finding(textQuote: quote)))!;

        Assert.Equal(quote, Assert.Single(findings).TextQuote);
    }

    [Fact]
    public void Quotes_are_kept_word_for_word_including_their_spaces()
    {
        const string quote = "  Zwei  Leerzeichen\nund ein Umbruch. ";

        var findings = FactCheckOutputParser.ParseCheck(Findings(Finding(textQuote: quote)))!;

        Assert.Equal(quote, Assert.Single(findings).TextQuote);
    }

    [Fact]
    public void More_than_twelve_findings_are_capped_in_their_order()
    {
        var many = Enumerable.Range(1, 15).Select(i => Finding(textQuote: $"Aussage Nummer {i} steht so im Text.")).ToArray();

        var findings = FactCheckOutputParser.ParseCheck(Findings(many))!;

        Assert.Equal(FactCheckOutputParser.MaxFindings, findings.Count);
        Assert.Equal("Aussage Nummer 1 steht so im Text.", findings[0].TextQuote);
        Assert.Equal("Aussage Nummer 12 steht so im Text.", findings[^1].TextQuote);
    }

    [Fact]
    public void Dropped_findings_do_not_count_towards_the_cap()
    {
        var many = new[] { Finding("unknown"), Finding(textQuote: new string('y', 700)) }
            .Concat(Enumerable.Range(1, 12).Select(i => Finding(textQuote: $"Aussage Nummer {i} steht so im Text.")))
            .ToArray();

        var findings = FactCheckOutputParser.ParseCheck(Findings(many))!;

        Assert.Equal(12, findings.Count);
        Assert.Equal("Aussage Nummer 12 steht so im Text.", findings[^1].TextQuote);
    }

    [Fact]
    public void Source_conflicts_have_their_own_cap_and_never_crowd_out_passages_of_the_text()
    {
        var conflicts = Enumerable.Range(1, 14).Select(i => Finding("source_conflict", null, material: $"Summe {i} Euro", material2: $"Posten {i} Euro"));
        var passages = Enumerable.Range(1, 3).Select(i => Finding(textQuote: $"Aussage Nummer {i} steht so im Text."));

        var parsed = FactCheckOutputParser.ParseCheckCounted(Findings([.. conflicts, .. passages]))!;

        Assert.Equal(3, parsed.Findings.Count(f => f.Kind == FactFindingKind.Unsupported));
        Assert.Equal(FactCheckOutputParser.MaxFindings, parsed.Findings.Count(f => f.Kind == FactFindingKind.SourceConflict));
        Assert.Equal(2, parsed.Dropped);
    }

    [Fact]
    public void Findings_over_the_cap_or_with_an_over_long_quote_are_counted_as_dropped_unknown_kinds_are_not()
    {
        var many = new[] { Finding("unknown"), Finding(textQuote: new string('y', 700)) }
            .Concat(Enumerable.Range(1, 14).Select(i => Finding(textQuote: $"Aussage Nummer {i} steht so im Text.")))
            .ToArray();

        var parsed = FactCheckOutputParser.ParseCheckCounted(Findings(many))!;

        Assert.Equal(FactCheckOutputParser.MaxFindings, parsed.Findings.Count);
        Assert.Equal(1 + 2, parsed.Dropped);
        Assert.Equal(0, FactCheckOutputParser.ParseCheckCounted(Valid)!.Dropped);
    }

    [Fact]
    public void What_is_wrong_is_cut_to_300_characters_on_one_line()
    {
        var reason = "Erste Zeile\n\nzweite   Zeile " + new string('z', 400);

        var finding = Assert.Single(FactCheckOutputParser.ParseCheck(Findings(Finding(whatIsWrong: reason)))!);

        Assert.Equal(FactCheckOutputParser.MaxWhatIsWrongChars, finding.WhatIsWrong.Length);
        Assert.StartsWith("Erste Zeile zweite Zeile zzz", finding.WhatIsWrong);
        Assert.DoesNotContain('\n', finding.WhatIsWrong);
    }

    [Fact]
    public void Null_and_missing_fields_become_null_and_a_missing_reason_empty()
    {
        const string json = """{"findings":[{"kind":"unsupported","text_quote":"Der Termin ist am Montag um zehn Uhr."}]}""";

        var finding = Assert.Single(FactCheckOutputParser.ParseCheck(json)!);

        Assert.Equal(new ProposedFactFinding(FactFindingKind.Unsupported, "Der Termin ist am Montag um zehn Uhr.", string.Empty, null, null, null), finding);
    }

    [Fact]
    public void Blank_quotes_and_quotes_of_the_wrong_type_become_null()
    {
        const string json = """{"findings":[{"kind":"instruction","text_quote":"  ","what_is_wrong":"x","instruction_quote":42,"material_quote":"","material_quote_2":null}]}""";

        var finding = Assert.Single(FactCheckOutputParser.ParseCheck(json)!);

        Assert.Null(finding.TextQuote);
        Assert.Null(finding.InstructionQuote);
        Assert.Null(finding.MaterialQuote);
    }

    [Fact]
    public void Items_that_are_not_objects_are_skipped()
    {
        var findings = FactCheckOutputParser.ParseCheck($$"""{"findings":["text", 3, null, {{Finding()}}]}""")!;

        Assert.Single(findings);
    }
}
