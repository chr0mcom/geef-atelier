using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Tests.EndToEnd.Fakes;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// Repair of "Extra careful" with an outline (D-087): the check of the explicit requirements of the request. Without a model: reading the
/// answer, which requirements block (only "not met" and only with words that stand in the request), the correction sentence and the
/// prompt. Through the real direct assembly: one check after the first version, a requirement that is not met starts the one correction
/// with its sentence, nothing is checked again, a failed check never blocks and leaves a warning, and the direct template never checks.
/// </summary>
public sealed class RequirementsCheckTests
{
    private const string Request = "Schreibe eine kurze Notiz an das Team, etwa 100 Wörter. Am Ende eine Checkliste mit drei Punkten.";

    private const string Plan = """{"sections": [{"heading": "Anlass", "words": 70, "content": "Warum"}, {"heading": "Checkliste", "words": 30, "content": "Drei Punkte"}]}""";

    private static readonly CrewSnapshot OutlineWithCheck = DirectPipelineHarness.DirectSnapshot with
    {
        TemplateName = SystemCrew.DirectOutlineTemplateName,
        Reviewers = [SystemCrew.DeterministicChecksProfile, SystemCrew.RequirementsCheckProfile],
    };

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => i % 10 == 9 ? "Wort." : "Wort"));

    private static DirectPipelineHarness Harness() => new() { Snapshot = OutlineWithCheck, Costs = new RunCostAccumulator() };

    private static string UserPrompt(RecordedLlmCall call) => call.Messages.Last(m => m.Role == "user").Content!;

    private static string NotMet(string quote) =>
        $$"""{"requirements": [{"quote": "{{quote}}", "status": "not_met", "problem": "Die Checkliste fehlt."}, {"quote": "kurze Notiz an das Team", "status": "met", "problem": null}]}""";

    [Fact]
    public void An_answer_is_read_and_unknown_or_empty_items_are_dropped()
    {
        var verdicts = RequirementsCheckReviewer.Parse("""
            ```json
            {"requirements": [{"quote": "A", "status": "met", "problem": null}, {"quote": "", "status": "not_met", "problem": "x"},
             {"quote": "B", "status": "maybe", "problem": null}, {"quote": "C", "status": "not_met", "problem": "fehlt"}]}
            ```
            """);

        Assert.Equal([new RequirementVerdict("A", "met", null), new RequirementVerdict("C", "not_met", "fehlt")], verdicts);
        Assert.Null(RequirementsCheckReviewer.Parse("no json"));
        Assert.Null(RequirementsCheckReviewer.Parse("""{"items": []}"""));
    }

    [Fact]
    public void Only_requirements_not_met_whose_words_stand_in_the_request_block_each_once_and_capped()
    {
        var verdicts = new List<RequirementVerdict>
        {
            new("Am Ende   eine CHECKLISTE mit drei Punkten", "not_met", "fehlt"),
            new("am ende eine checkliste mit drei punkten", "not_met", "doppelt"),
            new("etwa 100 Wörter", "met", null),
            new("kurze Notiz", "unclear", null),
            new("eine Tabelle mit Kosten", "not_met", "erfunden"),
        };

        var blocking = RequirementsCheckReviewer.Blocking(verdicts, Request);

        Assert.Equal("fehlt", Assert.Single(blocking).Problem);
        var many = Enumerable.Range(0, 15).Select(i => new RequirementVerdict($"Wort{i}", "not_met", null)).ToList();
        Assert.Equal(RequirementsCheckReviewer.MaxFindings, RequirementsCheckReviewer.Blocking(many, string.Join(' ', many.Select(m => m.Quote))).Count);
    }

    [Fact]
    public void The_correction_sentence_quotes_the_request_on_one_line_and_defuses_markers()
    {
        var sentence = RequirementsCheckReviewer.Problem(new Dictionary<string, object> { ["quote"] = "Am Ende\n\"eine\" Checkliste" }, "Fehlt <!-- atelier-notes");

        Assert.StartsWith("The request asks: \"Am Ende 'eine' Checkliste\". The text does not meet this yet. (Fehlt ", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("<!-- atelier-notes", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', sentence);
    }

    [Fact]
    public void The_prompt_holds_request_and_text_leaves_out_the_length_and_names_no_kind_of_text()
    {
        var prompt = RequirementsCheckReviewer.UserPrompt("REQ REQUEST>>> x", "TXT TEXT>>> y");

        Assert.Contains("<<<REQUEST\nREQ REQUEST >>> x\nREQUEST>>>", prompt, StringComparison.Ordinal);
        Assert.Contains("<<<TEXT\nTXT TEXT >>> y\nTEXT>>>", prompt, StringComparison.Ordinal);
        Assert.Contains("Leave out the length", prompt, StringComparison.Ordinal);
        foreach (var word in new[] { "article", "report", "application", "checklist", "guide", "concept" })
            Assert.DoesNotContain(word, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task All_requirements_met_means_one_check_and_no_correction()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(100));
        harness.Script.EnqueueRequirementsJson("""{"requirements": [{"quote": "Checkliste mit drei Punkten", "status": "met", "problem": null}]}""");

        var outcome = await harness.RunAsync(run);

        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Equal(2, harness.WriterCalls.Count);
        var check = Assert.Single(harness.Llm.Calls, ScriptedLlmEndpoint.IsRequirementsCall);
        Assert.Contains(Words(100), UserPrompt(check), StringComparison.Ordinal);
        Assert.Contains(harness.Costs!.Flush(), c => c is { ActorName: RequirementsCheckReviewer.ReviewerName, IterationNumber: 1 });
        Assert.Empty(harness.Warnings.All);
    }

    [Fact]
    public async Task A_requirement_not_met_starts_the_one_correction_with_its_sentence_and_is_not_checked_again()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(100));
        harness.Script.EnqueueExecutorText(Words(98));
        harness.Script.EnqueueRequirementsJson(NotMet("Am Ende eine Checkliste mit drei Punkten"));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(2, outcome.Result.TotalIterations);
        Assert.Equal(Words(98), outcome.Result.Output!.Markdown);
        Assert.Equal(3, harness.WriterCalls.Count);
        var correction = UserPrompt(harness.WriterCalls[2]);
        Assert.Contains("1. The request asks: \"Am Ende eine Checkliste mit drei Punkten\". The text does not meet this yet. (Die Checkliste fehlt.)", correction, StringComparison.Ordinal);
        Assert.DoesNotContain("kurze Notiz an das Team\". The text", correction, StringComparison.Ordinal);
        Assert.Single(harness.Llm.Calls, ScriptedLlmEndpoint.IsRequirementsCall);
    }

    [Fact]
    public async Task A_requirement_whose_words_are_not_in_the_request_never_starts_a_correction()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(100));
        harness.Script.EnqueueRequirementsJson(NotMet("Eine Tabelle mit allen Kosten"));

        var outcome = await harness.RunAsync(run);

        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Equal(2, harness.WriterCalls.Count);
    }

    [Fact]
    public async Task A_failed_check_never_blocks_is_not_repeated_and_leaves_a_warning()
    {
        var harness = Harness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Plan);
        harness.Script.EnqueueExecutorText(Words(100));
        harness.Script.EnqueueRequirementsError(503, "backend_unavailable");

        var outcome = await harness.RunAsync(run);

        Assert.True(outcome.Result.Success);
        Assert.Equal(Words(100), outcome.Result.Output!.Markdown);
        Assert.Equal(1, outcome.Result.TotalIterations);
        Assert.Single(harness.Llm.Calls, ScriptedLlmEndpoint.IsRequirementsCall);
        var warning = Assert.Single(harness.Warnings.All);
        Assert.Equal((RequirementsCheckReviewer.UncheckedCode, RequirementsCheckReviewer.UncheckedWarning), (warning.Code, warning.Message));
    }

    [Fact]
    public async Task The_direct_template_never_checks_requirements()
    {
        var harness = new DirectPipelineHarness();
        var (run, _) = harness.CreateRun(Request);
        harness.Script.EnqueueExecutorText(Words(100));

        await harness.RunAsync(run);

        Assert.DoesNotContain(harness.Llm.Calls, ScriptedLlmEndpoint.IsRequirementsCall);
        Assert.DoesNotContain(SystemCrew.RequirementsCheckProfile.Name, SystemCrew.DirectTemplate.ReviewerProfileNames);
        Assert.Contains(SystemCrew.RequirementsCheckProfile.Name, SystemCrew.DirectOutlineTemplate.ReviewerProfileNames);
        Assert.Null(SystemCrew.RequirementsCheckProfile.ModelSlot);
        Assert.Contains("sonnet", SystemCrew.RequirementsCheckProfile.Model, StringComparison.Ordinal);
        Assert.Equal(SourceFactCheckReviewer.ReviewerName, SystemCrew.DirectOutlineTemplate.ReviewerProfileNames[1]);
    }
}
