using System.Reflection;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// "What was checked" (contract stage 3, 4.7): the sentences for every state and every combination of checks, never a line for a
/// check that did not run, the fixed end always, and no jargon.
/// </summary>
public sealed class OrderCheckLinesTests
{
    private static readonly RunReportSource File = new(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400);

    private static readonly FactFinding Unbacked = new("f1", FactFindingKind.Unsupported, "Bitte geben Sie uns in der Gruppe Bescheid.", "x", null, null, null, null, null, true, QuoteMatchKind.Exact);
    private static readonly FactFinding Hint = new("f2", FactFindingKind.Instruction, "Liebe Eltern, viele Grüße", "x", "Use a friendly tone.", null, null, null, InstructionOrigin.Client, false, QuoteMatchKind.Exact);
    private static readonly FactFinding Conflict = new("f3", FactFindingKind.SourceConflict, null, "x", null, "Summe 62 €", "22 + 31 + 19", "notizen.txt", null, false, QuoteMatchKind.NotFound);

    private static DraftCheckResult Check(string code, bool blocking = true, bool passed = true) => new(code, passed, blocking, "ok", null);

    private static RunReport Report(
        IReadOnlyList<DraftCheckResult>? checks = null,
        FactCheckSummary? factCheck = null,
        SourceArithmeticResult? arithmetic = null,
        IReadOnlyList<RunReportCorrection>? corrections = null,
        IReadOnlyList<RunReportSource>? sources = null,
        OrderConstraints? constraints = null,
        int schema = RunReport.CurrentSchemaVersion) =>
        new(schema, RunReport.DirectKind, constraints ?? OrderConstraints.None,
            checks ?? [Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble)], [], sources ?? [File], 1, 1, null, [],
            WriterNotesStatus.Off, WriterNotes.Empty, [], 0, factCheck, arithmetic, corrections ?? []);

    private static FactCheckSummary Facts(FactCheckState state, FactCheckScope scope = FactCheckScope.Full, FactCheckMode mode = FactCheckMode.Report, params FactFinding[] open) =>
        new(mode, scope, state, open.Where(f => f.Kind != FactFindingKind.SourceConflict).ToList(), open.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList(), [], 1, "gpt-astra-latest", false, "fact-check-1");

    private static FactFinding Conflict2(string first, MaterialPart fromFirst, string second, MaterialPart fromSecond, string? file = null, string? file2 = null) =>
        new("f9", FactFindingKind.SourceConflict, null, "x", null, first, second, file ?? file2, null, false, QuoteMatchKind.NotFound, fromFirst, fromSecond, file2);

    [Fact]
    public void A_check_without_findings_names_the_checks_and_the_fact_check_and_ends_with_the_limits()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.Checked)), hasOpenPoints: false);

        Assert.Equal(["Checked automatically: the text is not empty, starts directly.", OrderCheckLines.FactsChecked, OrderCheckLines.Contradictions], view.Checked);
        Assert.Empty(view.Changed);
        Assert.Equal(OrderCheckLines.ToReviewNothing, view.ToReview);
        Assert.Equal(OrderCheckLines.Limits, view.Limits);
    }

    [Fact]
    public void Every_automatic_check_that_ran_is_named_and_no_other()
    {
        var checks = new[]
        {
            Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble), Check(DraftCheckCodes.Length), Check(DraftCheckCodes.Language),
            Check(DraftCheckCodes.Placeholders), Check(DraftCheckCodes.Lists), Check(DraftCheckCodes.Emojis), Check(DraftCheckCodes.ForbiddenTerm),
            Check(DraftCheckCodes.ForbiddenTerm), Check(DraftCheckCodes.RequiredTerm), Check(DraftCheckCodes.Arithmetic),
        };

        var lines = OrderCheckLines.Build(Report(checks), false).Checked;

        Assert.Equal("Checked automatically: the text is not empty, starts directly, has the requested length, is in the requested language, " +
                     "has no placeholders, avoids lists, emojis and the words you ruled out, uses the words you asked for.", lines[0]);
        Assert.Equal(OrderCheckLines.ArithmeticSearched, lines[1]);
    }

    [Fact]
    public void A_check_that_ran_and_did_not_pass_is_named_as_not_met_and_never_as_met()
    {
        var checks = new[]
        {
            Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble, blocking: false, passed: false), Check(DraftCheckCodes.Length, blocking: false, passed: false),
            Check(DraftCheckCodes.Language, passed: false), Check(DraftCheckCodes.Placeholders, passed: false), Check(DraftCheckCodes.Lists),
            Check(DraftCheckCodes.ForbiddenTerm), Check(DraftCheckCodes.ForbiddenTerm, passed: false), Check(DraftCheckCodes.RequiredTerm, passed: false),
            Check(DraftCheckCodes.Arithmetic, passed: false),
        };

        var view = OrderCheckLines.Build(Report(checks), true);

        Assert.Equal("Checked automatically: the text is not empty, avoids lists.", view.Checked[0]);
        Assert.Equal("Checked automatically and not met – see the open points below: the text may start with a remark to the reader, " +
                     "does not have the requested length, is not in the requested language, has placeholders, uses words you ruled out, " +
                     "lacks words you asked for, has a calculation that looks wrong.", view.Checked[1]);
        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderCheckLines.PassedLength, StringComparison.Ordinal)
            || l.Contains(OrderCheckLines.PassedStartsDirectly, StringComparison.Ordinal)
            || l == OrderCheckLines.ArithmeticSearched);
    }

    [Fact]
    public void A_length_missed_by_a_little_is_not_met_also_in_an_old_report()
    {
        var view = OrderCheckLines.Build(Report([Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Preamble), Check(DraftCheckCodes.Length, blocking: false, passed: false)], schema: 1), true);

        Assert.Equal(["Checked automatically: the text is not empty, starts directly.",
            "Checked automatically and not met – see the open points below: the text does not have the requested length.", OrderCheckLines.FactsNotChecked], view.Checked);
    }

    [Fact]
    public void Placeholders_to_fill_in_are_no_check_against_placeholders()
    {
        var line = OrderCheckLines.Build(Report([Check(DraftCheckCodes.Empty), Check(DraftCheckCodes.Placeholders, blocking: false)]), false).Checked[0];

        Assert.DoesNotContain("placeholders", line, StringComparison.Ordinal);
        Assert.DoesNotContain("length", line, StringComparison.Ordinal);
        Assert.DoesNotContain("calculations", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_without_checks_has_no_automatic_line()
    {
        var view = OrderCheckLines.Build(Report(checks: []), false);

        Assert.DoesNotContain(view.Checked, l => l.StartsWith("Checked automatically", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactCheckState.Off, FactCheckMode.Off, OrderCheckLines.FactsNotChecked)]
    [InlineData(FactCheckState.NotShown, FactCheckMode.Shadow, OrderCheckLines.FactsNotChecked)]
    [InlineData(FactCheckState.NotCompleted, FactCheckMode.Report, OrderCheckLines.FactsNotCompleted)]
    [InlineData(FactCheckState.VersionNotChecked, FactCheckMode.Report, OrderCheckLines.FactsVersionNotChecked)]
    [InlineData(FactCheckState.Checked, FactCheckMode.On, OrderCheckLines.FactsChecked)]
    public void The_fact_line_follows_the_state(FactCheckState state, FactCheckMode mode, string expected)
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(state, mode: mode)), false);

        Assert.Equal(expected, view.Checked[1]);
        Assert.Equal(state is FactCheckState.Checked, view.Checked.Contains(OrderCheckLines.Contradictions));
    }

    [Fact]
    public void Open_passages_are_counted()
    {
        Assert.Equal(string.Format(OrderCheckLines.FactsOpenPointsFormat, 2),
            OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.OpenPoints, open: [Unbacked, Hint])), true).Checked[1]);
        Assert.Equal(OrderCheckLines.FactsOpenPointOne,
            OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.OpenPoints, open: [Unbacked, Conflict])), true).Checked[1]);
    }

    [Fact]
    public void Without_files_the_fact_line_does_not_claim_files_and_there_is_no_contradiction_line()
    {
        var view = OrderCheckLines.Build(Report(sources: [], factCheck: Facts(FactCheckState.Checked)), false);

        Assert.Equal(OrderCheckLines.FactsCheckedNoFiles, view.Checked[1]);
        Assert.DoesNotContain(OrderCheckLines.Contradictions, view.Checked);
        Assert.Equal(OrderCheckLines.FactsOpenPointOneNoFiles,
            OrderCheckLines.Build(Report(sources: [], factCheck: Facts(FactCheckState.OpenPoints, open: [Unbacked])), true).Checked[1]);
    }

    [Fact]
    public void Files_too_long_say_that_facts_were_not_checked_and_claim_no_contradiction_check()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.Checked, FactCheckScope.WithoutFiles)), false);

        Assert.Equal("Your files were too long to compare with the text, so the facts in it were not checked. " +
                     "Only whether the text follows your instructions was checked; nothing was found against them.", view.Checked[1]);
        Assert.DoesNotContain(OrderCheckLines.Contradictions, view.Checked);
        Assert.DoesNotContain(view.Checked, l => l.StartsWith("Facts in the text were compared", StringComparison.Ordinal));
        Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, view.ToReview);
        Assert.EndsWith(" 1 passage needs your attention – see below.",
            OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.OpenPoints, FactCheckScope.WithoutFiles, open: [Hint])), true).Checked[1]);
    }

    [Fact]
    public void A_version_changed_after_the_check_says_its_facts_were_not_checked_and_names_earlier_passages_still_in_it()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.VersionNotChecked, open: [Unbacked])), true);

        Assert.Equal(OrderCheckLines.FactsVersionNotChecked + OrderCheckLines.FactsEarlierOne, view.Checked[1]);
        Assert.DoesNotContain(OrderCheckLines.Contradictions, view.Checked);
        Assert.Equal(OrderCheckLines.FactsNotCompleted + string.Format(OrderCheckLines.FactsEarlierFormat, 2),
            OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.NotCompleted, open: [Unbacked, Hint])), true).Checked[1]);
    }

    [Fact]
    public void Flagged_passages_that_could_not_be_listed_are_never_nothing_found()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.Checked) with { NotShown = 2 }), false);

        Assert.Equal(OrderCheckLines.FactsComparedLead + string.Format(OrderCheckLines.FactsNotShownOnlyFormat, 2), view.Checked[1]);
        Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, view.ToReview);
        Assert.Equal(string.Format(OrderCheckLines.FactsOpenPointsFormat, 8) + OrderCheckLines.FactsNotShownOne,
            OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.OpenPoints, open: Enumerable.Repeat(Unbacked, 8).ToArray()) with { NotShown = 1 }), true).Checked[1]);
    }

    [Fact]
    public void A_schema_one_report_says_facts_were_not_checked()
    {
        var view = OrderCheckLines.Build(Report(schema: 1), false);

        Assert.Equal(["Checked automatically: the text is not empty, starts directly.", OrderCheckLines.FactsNotChecked], view.Checked);
    }

    [Fact]
    public void Totals_are_named_only_when_files_had_totals_and_never_said_to_be_right()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Computed);

        Assert.DoesNotContain(OrderCheckLines.Build(Report(arithmetic: new SourceArithmeticResult(0, [])), false).Checked, l => l.Contains("total", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Your files were searched for totals that are clearly wrong, and none was found. Not every total can be checked this way.",
            OrderCheckLines.Build(Report(arithmetic: new SourceArithmeticResult(2, [])), false).Checked);
        Assert.Contains(OrderCheckLines.TotalsSearchedNone, OrderCheckLines.Build(Report(arithmetic: new SourceArithmeticResult(1, [])), false).Checked);
        Assert.Contains("Your files were searched for totals that are clearly wrong: 1 looks wrong – please check it below. Not every total can be checked this way.",
            OrderCheckLines.Build(Report(arithmetic: new SourceArithmeticResult(3, [issue])), true).Checked);
        Assert.DoesNotContain(typeof(OrderCheckLines).GetFields().Select(f => f.GetRawConstantValue() as string),
            t => t is not null && (t.Contains("all correct", StringComparison.Ordinal) || t.Contains(" add up", StringComparison.Ordinal)
                || t.Contains("adds up", StringComparison.Ordinal) || t.Contains("recalculated", StringComparison.Ordinal)));
    }

    [Fact]
    public void Totals_over_the_list_limit_are_counted_in_one_sentence()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither);
        var report = Report(arithmetic: new SourceArithmeticResult(14, [issue], 3));

        Assert.Equal(string.Format(OrderCheckLines.TotalsNotListedFormat, 3), OrderCheckLines.SourceIssueSentences(report)[^1]);
        Assert.Contains(string.Format(OrderCheckLines.TotalsSearchedFormat, 4), OrderCheckLines.Build(report, true).Checked);
    }

    [Theory]
    [InlineData(TotalUseInText.Computed, "Your file \"notizen.txt\" gives 62 € as the total, but 22 € + 31 € + 19 € = 72 €. The text uses 72 €. Please check which figure is right.")]
    [InlineData(TotalUseInText.Stated, "Your file \"notizen.txt\" gives 62 € as the total, but 22 € + 31 € + 19 € = 72 €. The text uses 62 €. Please check which figure is right.")]
    [InlineData(TotalUseInText.Neither, "Your file \"notizen.txt\" gives 62 € as the total, but 22 € + 31 € + 19 € = 72 €. The text does not mention the total. Please check the file.")]
    public void A_wrong_total_names_both_figures_and_which_one_the_text_uses(TotalUseInText use, string expected)
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", use);

        Assert.Equal([expected], OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]))));
    }

    [Fact]
    public void Contradictions_are_told_unless_the_fact_check_is_hidden()
    {
        Assert.Equal(["Your request, your answers or your files say “Summe 62 €” and “22 + 31 + 19”."],
            OrderCheckLines.SourceIssueSentences(Report(factCheck: Facts(FactCheckState.Checked, open: [Conflict]))));
        Assert.Empty(OrderCheckLines.SourceIssueSentences(Report(factCheck: Facts(FactCheckState.NotShown, mode: FactCheckMode.Shadow, open: [Conflict]))));
    }

    [Fact]
    public void Changes_after_the_check_name_every_correction_once()
    {
        var corrections = new[]
        {
            new RunReportCorrection(FactFindingMetadata.UnsupportedCode, "Bitte geben Sie uns in der Gruppe Bescheid."),
            new RunReportCorrection(FactFindingMetadata.SupersededCode, "am 7. März im Raum 4"),
            new RunReportCorrection(DraftCheckCodes.Length, null),
            new RunReportCorrection(DraftCheckCodes.Length, null),
            new RunReportCorrection(DraftCheckCodes.Emojis, null),
            new RunReportCorrection(FactFindingMetadata.InstructionCode, null),
            new RunReportCorrection("unknown.code", null),
        };
        var constraints = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, null, 50, "max. 50 Wörter", LengthKind.AtMost) };

        var view = OrderCheckLines.Build(Report(corrections: corrections, constraints: constraints), false);

        Assert.Equal(
        [
            "Removed or replaced: “Bitte geben Sie uns in der Gruppe Bescheid.”",
            "Removed or replaced: “am 7. März im Raum 4”",
            OrderCheckLines.ChangedLength,
            OrderCheckLines.ChangedEmojis,
        ], view.Changed);
    }

    [Theory]
    [InlineData(LengthKind.AtMost, OrderCheckLines.ChangedLength)]
    [InlineData(LengthKind.AtLeast, OrderCheckLines.ChangedLengthLonger)]
    [InlineData(LengthKind.Range, OrderCheckLines.ChangedLengthOther)]
    public void A_length_correction_says_the_right_direction(LengthKind kind, string expected)
    {
        var constraints = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, 100, 200, "100 Wörter", kind) };

        Assert.Equal([expected], OrderCheckLines.Build(Report(corrections: [new RunReportCorrection(DraftCheckCodes.Length, null)], constraints: constraints), false).Changed);
    }

    [Theory]
    [InlineData(MaterialPart.Request, MaterialPart.Files, "Your request says “am 12. Mai”, but your file \"plan.txt\" says “am 14. Mai”.")]
    [InlineData(MaterialPart.PersonAnswers, MaterialPart.Profile, "Your answers say “am 12. Mai”, but your About me details say “am 14. Mai”.")]
    [InlineData(MaterialPart.Files, MaterialPart.Files, "Your file \"plan.txt\" says “am 12. Mai” and also “am 14. Mai”.")]
    [InlineData(MaterialPart.AssistantAnswers, MaterialPart.Request, "Your assistant's answers say “am 12. Mai”, but your request says “am 14. Mai”.")]
    public void A_contradiction_names_where_each_side_stands(MaterialPart first, MaterialPart second, string expected)
    {
        var conflict = Conflict2("am 12. Mai", first, "am 14. Mai", second,
            first == MaterialPart.Files ? "plan.txt" : null, second == MaterialPart.Files ? "plan.txt" : null);

        Assert.Equal([expected],
            OrderCheckLines.SourceIssueSentences(Report(factCheck: Facts(FactCheckState.Checked, open: [conflict]))));
    }

    [Fact]
    public void A_contradiction_from_the_about_me_details_is_hidden_from_anyone_but_the_owner()
    {
        var conflict = Conflict2("am 12. Mai", MaterialPart.Request, "Ich arbeite nie montags", MaterialPart.Profile);

        var sentence = Assert.Single(OrderCheckLines.SourceIssueSentences(Report(factCheck: Facts(FactCheckState.Checked, open: [conflict])), showProfile: false));

        Assert.DoesNotContain("montags", sentence, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.FromProfileHidden, sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contradiction_that_repeats_a_wrong_total_is_told_once()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither);
        var conflict = Conflict2("Material zusammen 62 €", MaterialPart.Files, "Material 22 € + 31 € + 19 €", MaterialPart.Files, "notizen.txt", "notizen.txt");

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Contains("gives 62 €", Assert.Single(sentences), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Summe 62 € am Montag", "Deko 19 € am Dienstag")]
    [InlineData("zusammen 62 €", "Kleber 19 €")]
    [InlineData("Summe 62 €", "22 € + 31 € + 19 € bis Freitag")]
    public void A_contradiction_whose_passages_differ_in_more_than_figures_and_total_words_is_shown_beside_the_total(string first, string second)
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither);
        var conflict = Conflict2(first, MaterialPart.Files, second, MaterialPart.Files, "notizen.txt", "notizen.txt");

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Equal(2, sentences.Count);
        Assert.Contains(sentences, s => s.Contains(first, StringComparison.Ordinal) && s.Contains(second, StringComparison.Ordinal));
    }

    [Fact]
    public void A_contradiction_whose_figure_only_contains_the_total_is_still_shown()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 50 €", ["20 €", "40 €"], "50 €", "60 €", TotalUseInText.Neither);
        var conflict = Conflict2("Eintritt 150 €", MaterialPart.Files, "Eintritt 120 €", MaterialPart.Files, "notizen.txt", "notizen.txt");

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Equal(2, sentences.Count);
        Assert.Contains(sentences, s => s.Contains("Eintritt 150 €", StringComparison.Ordinal) && s.Contains("Eintritt 120 €", StringComparison.Ordinal));
    }

    [Fact]
    public void A_contradiction_between_request_and_answers_is_shown_although_it_names_the_figure_of_a_total()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 15", ["7", "9"], "15", "16", TotalUseInText.Neither);
        var conflict = Conflict2("Anmeldung bis 15. Mai", MaterialPart.Request, "Anmeldung bis 12. Mai", MaterialPart.PersonAnswers);

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Equal(2, sentences.Count);
        Assert.Contains("Your request says “Anmeldung bis 15. Mai”, but your answers say “Anmeldung bis 12. Mai”.", sentences);
    }

    [Fact]
    public void A_budget_in_the_request_against_the_total_of_a_file_is_shown()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe: 50 €", ["20 €", "40 €"], "50 €", "60 €", TotalUseInText.Neither);
        var conflict = Conflict2("höchstens 40 € ausgeben", MaterialPart.Request, "Summe: 50 €", MaterialPart.Files, null, "notizen.txt");

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Equal(2, sentences.Count);
        Assert.Contains(sentences, s => s.Contains("höchstens 40 € ausgeben", StringComparison.Ordinal));
    }

    [Fact]
    public void A_contradiction_from_another_file_or_without_known_origin_is_shown()
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither);
        var otherFile = Conflict2("zusammen 62 €", MaterialPart.Files, "Kleber 19 €", MaterialPart.Files, "notizen.txt", "budget.txt");
        var unknown = Conflict with { MaterialQuote = "zusammen 62 €", MaterialQuote2 = "Kleber 19 €" };

        Assert.Equal(2, OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [otherFile]))).Count);
        Assert.Equal(2, OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [unknown]))).Count);
    }

    [Fact]
    public void Files_too_long_with_passages_that_could_not_be_listed_never_say_nothing_was_found()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.OpenPoints, FactCheckScope.WithoutFiles) with { NotShown = 1 }), false);

        Assert.Equal(OrderCheckLines.FactsWithoutFilesLead + OrderCheckLines.FactsNotShownOnlyOne, view.Checked[1]);
        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderCheckLines.FactsWithoutFilesNothing.Trim(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactCheckState.NotCompleted, OrderCheckLines.FactsNotCompleted)]
    [InlineData(FactCheckState.VersionNotChecked, OrderCheckLines.FactsVersionNotChecked)]
    public void Passages_that_could_not_be_listed_are_named_also_when_the_delivered_wording_was_not_checked(FactCheckState state, string lead)
    {
        Assert.Equal(lead + string.Format(OrderCheckLines.FactsNotShownOnlyFormat, 2),
            OrderCheckLines.Build(Report(factCheck: Facts(state) with { NotShown = 2 }), false).Checked[1]);
        Assert.Equal(lead + OrderCheckLines.FactsEarlierOne + OrderCheckLines.FactsNotShownOne,
            OrderCheckLines.Build(Report(factCheck: Facts(state, open: Unbacked) with { NotShown = 1 }), true).Checked[1]);
    }

    [Theory]
    [InlineData(DraftCheckCodes.Arithmetic, true, "The calculations in the text add up.")]
    [InlineData(DraftCheckCodes.ArithmeticPartial, false, "The calculations in the text that could be read clearly add up; 1 other calculation(s) could not be read clearly and were not checked.")]
    public void Calculations_of_an_earlier_report_never_read_as_calculations_that_add_up(string code, bool blocking, string storedMessage)
    {
        var checks = new[] { Check(DraftCheckCodes.Empty), new DraftCheckResult(code, true, blocking, storedMessage, null) };

        var view = OrderCheckLines.Build(Report(checks: checks), false);

        Assert.Equal(["Checked automatically: the text is not empty.", OrderCheckLines.ArithmeticSearched], view.Checked.Take(2));
        Assert.DoesNotContain(view.Checked, l => l.Contains("add up", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_stored_earlier_report_with_passed_calculations_and_totals_never_reads_as_adding_up()
    {
        var stored = RunReportJson.TryParse("""
            {"schemaVersion":2,"kind":"direct","constraints":{},
             "checks":[{"code":"empty","passed":true,"blocking":true,"message":"ok"},{"code":"arithmetic","passed":true,"blocking":true,"message":"The calculations in the text add up."}],
             "placeholders":[],"sources":[],"deliveredIteration":1,"iterations":1,"notes":[],
             "sourceArithmetic":{"totalsFound":3,"issues":[],"issuesNotListed":0}}
            """)!;

        var view = OrderCheckLines.Build(stored, false);

        Assert.Equal(3, stored.SourceArithmetic!.TotalsFound);
        Assert.Contains(OrderCheckLines.ArithmeticSearched, view.Checked);
        Assert.Contains(OrderCheckLines.TotalsSearchedNone, view.Checked);
        Assert.DoesNotContain(view.Checked, l => l.Contains("add up", StringComparison.OrdinalIgnoreCase) || l.Contains("recalculated", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactCheckState.VersionNotChecked, true)]
    [InlineData(FactCheckState.NotCompleted, true)]
    [InlineData(FactCheckState.Checked, false)]
    [InlineData(FactCheckState.OpenPoints, false)]
    public void A_removed_passage_says_it_may_remain_in_other_words_when_the_new_wording_was_not_checked(FactCheckState state, bool mayRemain)
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(state), corrections: [new RunReportCorrection(FactFindingMetadata.UnsupportedCode, "Die Gebühr beträgt 15 Euro pro Kind und Tag.")]), false);

        Assert.Equal(string.Format(OrderCheckLines.ChangedRemovedFormat, "Die Gebühr beträgt 15 Euro pro Kind und Tag."), view.Changed[0]);
        Assert.Equal(mayRemain, view.Changed.Contains(OrderCheckLines.ChangedMayRemain));
    }

    [Fact]
    public void A_removal_of_a_check_without_a_quote_needs_no_note_about_other_words()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.VersionNotChecked), corrections: [new RunReportCorrection(DraftCheckCodes.Length, null)],
            constraints: OrderConstraints.None), false);

        Assert.DoesNotContain(OrderCheckLines.ChangedMayRemain, view.Changed);
    }

    [Fact]
    public void Open_points_ask_the_person_to_look_below_and_nothing_to_look_at_needs_a_full_fact_check()
    {
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, OrderCheckLines.Build(Report(), hasOpenPoints: true).ToReview);
        Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, OrderCheckLines.Build(Report(), hasOpenPoints: false).ToReview);
        Assert.Equal(OrderCheckLines.ToReviewNothing, OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.Checked)), hasOpenPoints: false).ToReview);
        foreach (var state in new[] { FactCheckState.Off, FactCheckState.NotShown, FactCheckState.NotCompleted, FactCheckState.VersionNotChecked })
            Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, OrderCheckLines.Build(Report(factCheck: Facts(state)), hasOpenPoints: false).ToReview);
    }

    [Fact]
    public void No_sentence_uses_jargon()
    {
        var sentences = typeof(OrderCheckLines).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Text: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.True(sentences.Count > 30);
        Assert.All(sentences, s => Assert.False(JargonBlocklist.Contains(s.Text), s.Name));
    }

    private static readonly RunReportSource LookedUpSource = new(null, "Looked up on the web", SourceUsageMode.FullText, 900, LookedUp: true);

    [Theory]
    [InlineData(true, false, "Facts in the text were compared with your request, your answers and your files. Nothing was found that they do not back.")]
    [InlineData(false, false, "Facts in the text were compared with your request and your answers. Nothing was found that they do not back.")]
    [InlineData(true, true, "Facts in the text were compared with your request, your answers, your files and the pages looked up. Nothing was found that they do not back.")]
    [InlineData(false, true, "Facts in the text were compared with your request, your answers and the pages looked up. Nothing was found that they do not back.")]
    public void The_line_about_the_facts_has_four_versions_and_names_the_pages_looked_up_only_when_the_run_had_them(bool files, bool lookedUp, string expected)
    {
        IReadOnlyList<RunReportSource> sources = (files ? new[] { File } : []).Concat(lookedUp ? new[] { LookedUpSource } : []).ToList();
        var report = Report(factCheck: Facts(FactCheckState.Checked), sources: sources) with { LookedUp = lookedUp };

        Assert.Contains(expected, OrderCheckLines.Build(report, hasOpenPoints: false).Checked);
    }

    [Fact]
    public void The_line_about_the_facts_with_the_pages_looked_up_keeps_the_count_of_passages()
    {
        var report = Report(factCheck: Facts(FactCheckState.OpenPoints, open: [Unbacked]), sources: [File, LookedUpSource]) with { LookedUp = true };

        Assert.Contains(OrderResearchLines.FactsComparedLeadLookedUp + OrderCheckLines.FactsOpenOne, OrderCheckLines.Build(report, hasOpenPoints: true).Checked);
    }

    [Fact]
    public void The_pages_looked_up_never_count_as_files_of_the_person()
    {
        var report = Report(factCheck: Facts(FactCheckState.Checked), sources: [LookedUpSource]) with { LookedUp = true };

        var view = OrderCheckLines.Build(report, hasOpenPoints: false);

        Assert.Contains(OrderResearchLines.FactsComparedLeadLookedUpNoFiles + OrderCheckLines.FactsNothingFound, view.Checked);
        Assert.DoesNotContain(OrderCheckLines.Contradictions, view.Checked);
        Assert.False(OrderCheckLines.ComparedFiles(report));
    }

    [Fact]
    public void Without_the_pages_in_the_run_no_line_mentions_them_even_if_an_old_source_is_marked()
    {
        var view = OrderCheckLines.Build(Report(factCheck: Facts(FactCheckState.Checked), sources: [File]), hasOpenPoints: false);

        Assert.DoesNotContain(view.Checked, l => l.Contains(OrderResearchLines.FromLookedUp, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MaterialPart.LookedUp, MaterialPart.LookedUp, "The pages looked up say “Mindestlohn 13,90 €” and also “Mindestlohn 12,82 €”.")]
    [InlineData(MaterialPart.Files, MaterialPart.LookedUp, "Your file \"plan.txt\" says “Mindestlohn 13,90 €”, but the pages looked up say “Mindestlohn 12,82 €”.")]
    [InlineData(MaterialPart.LookedUp, MaterialPart.Request, "The pages looked up say “Mindestlohn 13,90 €”, but your request says “Mindestlohn 12,82 €”.")]
    public void A_contradiction_with_the_pages_looked_up_names_them_and_never_a_file(MaterialPart first, MaterialPart second, string expected)
    {
        var conflict = Conflict2("Mindestlohn 13,90 €", first, "Mindestlohn 12,82 €", second,
            first == MaterialPart.Files ? "plan.txt" : first == MaterialPart.LookedUp ? "Looked up on the web" : null,
            second == MaterialPart.Files ? "plan.txt" : second == MaterialPart.LookedUp ? "Looked up on the web" : null);

        var sentence = Assert.Single(OrderCheckLines.SourceIssueSentences(Report(factCheck: Facts(FactCheckState.Checked, open: [conflict]))));

        Assert.Equal(expected, sentence);
        Assert.DoesNotContain("Looked up on the web", sentence, StringComparison.Ordinal);
    }

    private const string CostLine = "Bastelkosten: Papier 22 €, Farben 31 €, Kleber 19 €, zusammen 62 €";

    private static SourceArithmeticIssue CostIssue()
    {
        var result = SourceArithmeticCheck.Check([("S1", "notizen.txt", "Treffen am Freitag\n- " + CostLine + "\n- Plätzchen ohne Nüsse")]);
        return Assert.Single(result.Issues);
    }

    [Fact]
    public void A_contradiction_between_the_items_and_the_total_inside_the_passage_of_a_wrong_total_is_told_once()
    {
        var issue = CostIssue();
        Assert.Contains("zusammen 62 €", issue.Quote, StringComparison.Ordinal);
        var conflict = Conflict2("Papier 22 €, Farben 31 €, Kleber 19 €", MaterialPart.Files, "zusammen 62 €", MaterialPart.Files, "notizen.txt", "notizen.txt");
        var swapped = Conflict2("zusammen 62 €", MaterialPart.Files, "Papier 22 €, Farben 31 €, Kleber 19 €", MaterialPart.Files, "notizen.txt", "notizen.txt");

        foreach (var finding in new[] { conflict, swapped })
        {
            var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [finding])));

            Assert.Equal([OrderCheckLines.TotalSentence(issue)], sentences);
            Assert.StartsWith("Your file \"notizen.txt\" gives 62 € as the total, but 22 € + 31 € + 19 € = 72 €.", sentences[0], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Kleber 19 €", "zusammen 62 €")]
    [InlineData("Papier 22 €, Farben 31 €, Kleber 19 €, Porto 5 €", "zusammen 62 €")]
    [InlineData("Papier 22 €, Farben 31 €, Kleber 19 €", "zusammen 62 € am Montag")]
    [InlineData("Papier 22 €, Farben 31 €, Kleber 19 €", "Summe 62 €")]
    public void A_contradiction_that_says_more_than_the_wrong_total_is_still_shown_beside_it(string items, string total)
    {
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.SentenceTotal,
            "Bastelkosten: Papier 22 €, Farben 31 €, Kleber 19 €, Porto 5 €, zusammen 62 € am Montag", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Neither);
        var conflict = Conflict2(items, MaterialPart.Files, total, MaterialPart.Files, "notizen.txt", "notizen.txt");

        var sentences = OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [conflict])));

        Assert.Equal(2, sentences.Count);
        Assert.Contains(sentences, s => s.Contains(items, StringComparison.Ordinal) && s.Contains(total, StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_passages_from_the_pages_looked_up_or_another_file_are_still_shown_beside_the_total()
    {
        var issue = CostIssue();
        var web = Conflict2("Papier 22 €, Farben 31 €, Kleber 19 €", MaterialPart.LookedUp, "zusammen 62 €", MaterialPart.Files, "Looked up on the web", "notizen.txt");
        var other = Conflict2("Papier 22 €, Farben 31 €, Kleber 19 €", MaterialPart.Files, "zusammen 62 €", MaterialPart.Files, "budget.txt", "notizen.txt");

        Assert.Equal(2, OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [web]))).Count);
        Assert.Equal(2, OrderCheckLines.SourceIssueSentences(Report(arithmetic: new SourceArithmeticResult(1, [issue]), factCheck: Facts(FactCheckState.Checked, open: [other]))).Count);
    }
}
