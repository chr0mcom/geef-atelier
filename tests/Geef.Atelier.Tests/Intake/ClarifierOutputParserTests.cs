using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Infrastructure.Intake;

namespace Geef.Atelier.Tests.Intake;

public sealed class ClarifierOutputParserTests
{
    internal const string Valid = """
        {
          "text_type": "Einladung",
          "language": "de",
          "understanding": "Eine Einladung zum Sommerfest des Teams.",
          "is_revision": false,
          "revision_file": null,
          "length_scale": "short_form",
          "gaps": [
            { "topic": "length", "resolution": "ask", "user_can_know": true, "web_researchable": false, "impact": "high",
              "assumption": "Etwa 150 Wörter", "confidence": "unsure", "question": null },
            { "topic": "audience", "resolution": "ask", "user_can_know": true, "web_researchable": false, "impact": "high",
              "assumption": "Das ganze Team", "confidence": "unsure",
              "question": { "text": "Wer soll kommen?", "options": ["Nur das Team", "Mit Familien"], "default_option": 1, "allow_free_text": false } },
            { "topic": "facts", "resolution": "research", "user_can_know": false, "web_researchable": true, "impact": "low",
              "assumption": "", "confidence": "sure" }
          ],
          "research_topics": ["Sommerfest Ideen"]
        }
        """;

    [Fact]
    public void Reads_a_valid_answer()
    {
        var output = ClarifierOutputParser.Parse(Valid);

        Assert.NotNull(output);
        Assert.Equal(("Einladung", "de", "Eine Einladung zum Sommerfest des Teams.", false, (int?)null, LengthScale.ShortForm),
            (output.TextType, output.Language, output.Understanding, output.IsRevision, output.RevisionFileNumber, output.LengthScale));
        Assert.Equal(3, output.Gaps.Count);
        Assert.Equal(new IntakeGap(GapTopic.Length, GapResolution.Ask, true, false, GapImpact.High, "Etwa 150 Wörter", AssumptionConfidence.Unsure, null), output.Gaps[0]);
        var question = output.Gaps[1].Question!;
        Assert.Equal(("Wer soll kommen?", 0, false), (question.Text, question.DefaultIndex, question.AllowFreeText));
        Assert.Equal(["Nur das Team", "Mit Familien"], question.Options);
        Assert.Null(output.Gaps[2].Assumption);
        Assert.Equal(GapResolution.Research, output.Gaps[2].Resolution);
        Assert.Equal(["Sommerfest Ideen"], output.ResearchTopics);
    }

    [Fact]
    public void Unknown_enum_values_become_safe_defaults()
    {
        var output = ClarifierOutputParser.Parse("""
            {"language":"en","understanding":"A post.","length_scale":"huge","gaps":[
              {"topic":"weather","resolution":"guess","impact":"medium","confidence":"maybe","assumption":"x"},
              {"topic":"3","resolution":"4"}]}
            """);

        Assert.NotNull(output);
        Assert.Equal(LengthScale.Standard, output.LengthScale);
        Assert.Equal(string.Empty, output.TextType);
        Assert.All(output.Gaps, g =>
        {
            Assert.Equal(GapTopic.Other, g.Topic);
            Assert.Equal(GapResolution.Assume, g.Resolution);
            Assert.Equal(GapImpact.Low, g.Impact);
            Assert.Equal(AssumptionConfidence.Unsure, g.Confidence);
            Assert.False(g.UserCanKnow);
        });
        Assert.Empty(output.ResearchTopics);
    }

    [Theory]
    [InlineData("""{"language":"de","gaps":[]}""")]
    [InlineData("""{"understanding":"  ","language":"de","gaps":[]}""")]
    [InlineData("""{"understanding":"x","gaps":[]}""")]
    [InlineData("""{"understanding":"x","language":"Deutsch","gaps":[]}""")]
    [InlineData("""{"understanding":"x","language":"de"}""")]
    [InlineData("""{"understanding":"x","language":"de","gaps":{}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Missing_required_fields_give_null(string json) => Assert.Null(ClarifierOutputParser.Parse(json));

    [Fact]
    public void Oversized_strings_are_capped()
    {
        var long500 = new string('u', 500);
        var json = $$"""
            {"language":"DE","understanding":"{{long500}}","text_type":"{{long500}}","gaps":[
              {"topic":"audience","resolution":"ask","assumption":"{{long500}}",
               "question":{"text":"{{long500}}","options":["{{long500}}"] } }],
             "research_topics":["{{long500}}"]}
            """;

        var output = ClarifierOutputParser.Parse(json)!;

        Assert.Equal("de", output.Language);
        Assert.Equal(ClarifierOutputParser.MaxUnderstandingChars, output.Understanding.Length);
        Assert.Equal(ClarifierOutputParser.MaxTextTypeChars, output.TextType.Length);
        Assert.Equal(ClarifierOutputParser.MaxAssumptionChars, output.Gaps[0].Assumption!.Length);
        Assert.Equal(ClarifierOutputParser.MaxQuestionChars, output.Gaps[0].Question!.Text.Length);
        Assert.Equal(ClarifierOutputParser.MaxOptionChars, output.Gaps[0].Question!.Options[0].Length);
        Assert.Equal(ClarifierOutputParser.MaxResearchTopicChars, output.ResearchTopics[0].Length);
    }

    [Fact]
    public void A_long_assumption_is_cut_at_a_word_end_and_marked()
    {
        var long400 = string.Join(' ', Enumerable.Repeat("Fakten", 60));

        var output = ClarifierOutputParser.Parse(
            $$"""{"language":"de","understanding":"x","gaps":[{"topic":"facts","resolution":"assume","assumption":"{{long400}}"}]}""")!;

        var assumption = output.Gaps[0].Assumption!;
        Assert.True(assumption.Length <= ClarifierOutputParser.MaxAssumptionChars, assumption.Length.ToString());
        Assert.EndsWith("Fakten…", assumption);
    }

    [Fact]
    public void The_schema_never_limits_the_length_of_a_text_because_the_gateway_would_reject_the_whole_answer()
    {
        Assert.DoesNotContain("maxLength", ClarifierSchema.Json);
    }

    [Fact]
    public void More_than_eight_gaps_and_five_topics_are_cut()
    {
        var gaps = string.Join(',', Enumerable.Range(0, 12).Select(i => $$"""{"topic":"other","resolution":"assume","assumption":"a{{i}}"}"""));
        var topics = string.Join(',', Enumerable.Range(0, 9).Select(i => $"\"t{i}\""));

        var output = ClarifierOutputParser.Parse($$"""{"language":"de","understanding":"x","gaps":[{{gaps}}],"research_topics":[{{topics}}]}""")!;

        Assert.Equal(ClarifierOutputParser.MaxGaps, output.Gaps.Count);
        Assert.Equal("a7", output.Gaps[^1].Assumption);
        Assert.Equal(ClarifierOutputParser.MaxResearchTopics, output.ResearchTopics.Count);
    }

    [Fact]
    public void Json_inside_a_code_fence_is_read()
    {
        Assert.NotNull(ClarifierOutputParser.Parse($"```json\n{Valid}\n```"));
    }

    [Fact]
    public void A_default_option_of_zero_or_text_is_ignored_and_free_text_defaults_to_true()
    {
        var output = ClarifierOutputParser.Parse("""
            {"language":"de","understanding":"x","gaps":[
              {"topic":"audience","resolution":"ask","question":{"text":"Q?","options":["A","B"],"default_option":0}},
              {"topic":"purpose","resolution":"ask","question":{"text":"Q?","options":["A",5,"B"],"default_option":"2"}}]}
            """)!;

        Assert.Null(output.Gaps[0].Question!.DefaultIndex);
        Assert.True(output.Gaps[0].Question!.AllowFreeText);
        Assert.Equal(1, output.Gaps[1].Question!.DefaultIndex);
        Assert.Equal(["A", "", "B"], output.Gaps[1].Question!.Options);
    }
}
