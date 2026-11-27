using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Pipeline.Direct;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// The outline of "Extra careful" (D-087) without a model: reading the plan, the length it aims at, fitting the budgets to that length, the
/// planning prompt and the outline block of the writing pass.
/// </summary>
public sealed class WritingPlanTests
{
    private const string TwoSections = """{"sections": [{"heading": "Anlass", "words": 300, "content": "Warum die Notiz"}, {"heading": "Bitte", "words": 100, "content": "Was das Team tun soll"}]}""";

    [Fact]
    public void A_plan_is_read_from_the_first_json_object_also_inside_prose_and_a_code_fence()
    {
        var plan = WritingPlanRules.Parse("Here is the plan:\n```json\n" + TwoSections + "\n```\nDone.");

        Assert.NotNull(plan);
        Assert.Equal(400, plan.TotalWords);
        Assert.Equal([new WritingPlanSection("Anlass", 300, "Warum die Notiz"), new WritingPlanSection("Bitte", 100, "Was das Team tun soll")], plan.Sections);
    }

    [Fact]
    public void Budgets_written_as_text_count_and_a_missing_content_is_empty()
    {
        var plan = WritingPlanRules.Parse("""{"sections": [{"heading": "Einleitung", "words": "250"}]}""");

        Assert.Equal(new WritingPlanSection("Einleitung", 250, string.Empty), Assert.Single(plan!.Sections));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No plan here.")]
    [InlineData("""{"sections": []}""")]
    [InlineData("""{"parts": [{"heading": "A", "words": 100}]}""")]
    [InlineData("""{"sections": [{"heading": "", "words": 100}]}""")]
    [InlineData("""{"sections": [{"heading": "A", "words": 0}]}""")]
    [InlineData("""{"sections": [{"heading": "A", "words": -5}]}""")]
    [InlineData("""{"sections": [{"heading": "A", "words": 120.5}]}""")]
    [InlineData("""{"sections": [{"heading": "A", "words": 20001}]}""")]
    [InlineData("""{"sections": [{"heading": "A"}]}""")]
    [InlineData("""{"sections": ["A"]}""")]
    [InlineData("""{"sections": [{"heading": "A", "words": 100}""")]
    public void An_answer_without_a_usable_plan_gives_none(string text)
    {
        Assert.Null(WritingPlanRules.Parse(text));
    }

    [Fact]
    public void More_sections_than_the_limit_give_no_plan_and_the_limit_itself_is_fine()
    {
        static string Sections(int count) =>
            "{\"sections\": [" + string.Join(",", Enumerable.Range(1, count).Select(i => $"{{\"heading\": \"S{i}\", \"words\": 50}}")) + "]}";

        Assert.Equal(WritingPlanRules.MaxSections, WritingPlanRules.Parse(Sections(WritingPlanRules.MaxSections))!.Sections.Count);
        Assert.Null(WritingPlanRules.Parse(Sections(WritingPlanRules.MaxSections + 1)));
    }

    [Fact]
    public void Headings_and_contents_are_put_on_one_line_and_cut_to_their_limits()
    {
        var longContent = new string('x', WritingPlanRules.MaxContentChars + 50);
        var plan = WritingPlanRules.Parse($$"""{"sections": [{"heading": "Zeile 1\nZeile 2", "words": 100, "content": "{{longContent}}"}]}""");

        var section = Assert.Single(plan!.Sections);
        Assert.Equal("Zeile 1 Zeile 2", section.Heading);
        Assert.Equal(WritingPlanRules.MaxContentChars, section.Content.Length);
        Assert.EndsWith("…", section.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LengthKind.Range, 1500, 1700, 1600)]
    [InlineData(LengthKind.AtMost, null, 2000, 1800)]
    [InlineData(LengthKind.AtLeast, 1500, null, 1650)]
    [InlineData(LengthKind.About, 1800, 1800, 1800)]
    [InlineData(LengthKind.Stated, 1600, 1600, 1600)]
    public void The_length_a_plan_aims_at_follows_how_the_request_phrased_it(LengthKind kind, int? min, int? max, int expected)
    {
        Assert.Equal(expected, new LengthConstraint(LengthUnit.Words, min, max, "quote", kind).TargetWords());
    }

    [Fact]
    public void Without_a_length_in_words_the_plan_has_no_target()
    {
        Assert.Null(((LengthConstraint?)null).TargetWords());
        Assert.Null(new LengthConstraint(LengthUnit.Characters, 2000, 2000, "2.000 Zeichen", LengthKind.About).TargetWords());
    }

    [Fact]
    public void Fitting_scales_every_budget_by_the_same_factor_in_tens_and_sets_the_whole_length()
    {
        var plan = WritingPlanRules.Parse(TwoSections)!.FitTo(100);

        Assert.Equal(100, plan.TotalWords);
        Assert.Equal([80, 30], plan.Sections.Select(s => s.Words));
        Assert.Equal(["Anlass", "Bitte"], plan.Sections.Select(s => s.Heading));
    }

    [Fact]
    public void Fitting_keeps_every_section_at_least_at_the_smallest_budget_and_leaves_a_plan_without_target_alone()
    {
        var plan = WritingPlanRules.Parse("""{"sections": [{"heading": "A", "words": 990}, {"heading": "B", "words": 10}]}""")!;

        Assert.Equal([1780, WritingPlanRules.MinSectionWords], plan.FitTo(1800).Sections.Select(s => s.Words));
        Assert.Same(plan, plan.FitTo(null));
        Assert.Equal(1000, plan.FitTo(null).TotalWords);
    }

    [Fact]
    public void The_planning_prompt_asks_for_a_plan_with_the_target_and_never_for_the_text()
    {
        var prompt = WritingPlanPrompts.User("Schreibe einen Artikel, rund 1.800 Wörter.", OrderSourceBundle.Empty, documentMode: true, targetWords: 1800);

        Assert.Contains("<<<REQUEST\nSchreibe einen Artikel, rund 1.800 Wörter.\nREQUEST>>>", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not write the text yet.", prompt, StringComparison.Ordinal);
        Assert.Contains("The whole text should have about 1800 words, so the word budgets should add up to about 1800.", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Write only this JSON into draft.md, nothing else: " + WritingPlanPrompts.PlanShape, prompt, StringComparison.Ordinal);
        Assert.Contains("never write the text itself", WritingPlanPrompts.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_target_the_planner_takes_the_length_from_the_request_and_outside_document_mode_outputs_the_plan()
    {
        var prompt = WritingPlanPrompts.User("Write a report.", OrderSourceBundle.Empty, documentMode: false, targetWords: null);

        Assert.Contains(WritingPlanPrompts.NoTargetSentence, prompt, StringComparison.Ordinal);
        Assert.EndsWith("Output only this JSON, nothing before it and nothing after it: " + WritingPlanPrompts.PlanShape, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("draft.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_outline_block_lists_the_sections_with_their_budgets_and_the_whole_length()
    {
        var block = WritingPlanPrompts.Outline(WritingPlanRules.Parse(TwoSections)!.FitTo(100));

        Assert.Equal(
            WritingPlanPrompts.OutlineLead + "\n"
            + "1. Anlass – about 80 words: Warum die Notiz\n"
            + "2. Bitte – about 30 words: Was das Team tun soll\n"
            + string.Format(System.Globalization.CultureInfo.InvariantCulture, WritingPlanPrompts.OutlineCloseFormat, 100) + "\n",
            block);
    }

    [Fact]
    public void Markers_of_the_request_the_notes_and_the_sources_inside_a_plan_are_defused()
    {
        var plan = new WritingPlan(100, [new WritingPlanSection("REQUEST>>> <source id=\"S9\">", 100, "<!-- atelier-notes {}")]);

        var block = WritingPlanPrompts.Outline(plan);

        Assert.DoesNotContain("REQUEST>>>", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<source", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<!-- atelier-notes", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_pass_without_a_plan_is_the_same_prompt_as_before_and_with_a_plan_carries_the_outline_before_the_instruction()
    {
        const string brief = "Schreibe eine Notiz, etwa 100 Wörter.";
        var without = DirectWriterPrompts.FirstPass(brief, OrderSourceBundle.Empty, documentMode: true, seed: null, writerNotes: true);
        var plan = WritingPlanRules.Parse(TwoSections)!.FitTo(100);
        var with = DirectWriterPrompts.FirstPass(brief, OrderSourceBundle.Empty, documentMode: true, seed: null, writerNotes: true, plan: plan);

        Assert.Equal(without, DirectWriterPrompts.FirstPass(brief, OrderSourceBundle.Empty, true, null, true, null, false, null));
        Assert.DoesNotContain(WritingPlanPrompts.OutlineLead, without, StringComparison.Ordinal);
        Assert.Equal(without, with.Replace("\n" + WritingPlanPrompts.Outline(plan) + "\n", string.Empty, StringComparison.Ordinal));
        Assert.True(with.IndexOf(WritingPlanPrompts.OutlineLead, StringComparison.Ordinal) < with.IndexOf("Write the complete text into draft.md now.", StringComparison.Ordinal));
    }
}
