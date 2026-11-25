using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// With <see cref="QuestionGateInput.DropGuessedDates"/> the gate drops clickable answers that name a date, month or weekday the request
/// and the files do not name; options with dates from the order stay, durations and amounts stay, and fewer than two left turn the
/// question into its assumption. Without the switch the gate is unchanged.
/// </summary>
public sealed class QuestionGateCalendarTests
{
    private const string ClosedOverHolidays =
        "Write a short email to the tenants of our housing association: the office is closed between Christmas and New Year 2026.";

    private const string ReopenAssumption = "Closed from 24 December 2026, open again on Monday 4 January 2027.";

    private static IntakeGap ReopenGap() => QuestionGateTests.Ask(
        GapTopic.Date,
        "When does the office open again?",
        ["Monday, 4 January 2027", "Tuesday, 5 January 2027", "Saturday, 2 January 2027"],
        assumption: ReopenAssumption,
        defaultIndex: 0);

    private static QuestionGateResult Run(
        IntakeGap gap,
        string request,
        string language = "en",
        IReadOnlyList<string>? files = null,
        bool drop = true) =>
        QuestionGate.Apply(new QuestionGateInput(
            QuestionGateTests.Output([gap], language), request, false, new Dictionary<GapTopic, MemoryAnswer>(), 3, files, drop));

    [Fact]
    public void Guessed_reopening_days_of_the_order_are_not_offered_and_the_assumption_stays_visible()
    {
        var result = Run(ReopenGap(), ClosedOverHolidays);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedGuessedDate, Assert.Single(result.Trace).Decision);
        Assert.Equal(ReopenAssumption, Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void Without_the_switch_the_same_question_is_asked_as_before()
    {
        var question = Assert.Single(Run(ReopenGap(), ClosedOverHolidays, drop: false).Questions);

        Assert.Equal(3, question.Options.Count);
        Assert.Contains(question.Options, o => o.Label == "Saturday, 2 January 2027");
    }

    [Fact]
    public void A_weekday_not_named_anywhere_drops_only_that_option_and_the_default_follows()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Wann soll der Flohmarkt stattfinden?",
            ["Am 12. Mai", "Am 19. Mai", "Samstag, 26. Mai"], defaultIndex: 1);

        var question = Assert.Single(Run(gap, "Schreib einen Aushang für den Flohmarkt am 12. Mai oder am 19. Mai.", "de").Questions);

        Assert.Equal(["Am 12. Mai", "Am 19. Mai"], question.Options.Select(o => o.Label));
        Assert.Equal(question.Options[1].Id, question.DefaultOptionId);
    }

    [Fact]
    public void A_weekday_from_the_request_stays()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Wann am Samstag beginnt die Feier?",
            ["Samstag ab 15 Uhr", "Samstag ab 18 Uhr"]);

        Assert.Equal(2, Assert.Single(Run(gap, "Schreib eine Einladung zur Feier am Samstag.", "de").Questions).Options.Count);
    }

    [Fact]
    public void A_date_from_a_file_stays_also_in_another_notation()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Which date should the letter name?",
            ["12 May 2027", "19 May 2027"]);

        var question = Assert.Single(Run(gap, "Write a letter about the meeting in the attached notes.", files: ["Meeting: 12.05.2027, backup date 2027-05-19."]).Questions);

        Assert.Equal(2, question.Options.Count);
    }

    [Fact]
    public void The_weekday_of_a_full_date_of_the_request_counts_as_named()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Which day should the invitation name?",
            ["Wednesday, 12 May 2027", "Wednesday, 19 May 2027"]);

        var question = Assert.Single(Run(gap, "Write an invitation for 12.5.2027 or 19.5.2027.").Questions);

        Assert.Equal(2, question.Options.Count);
    }

    [Fact]
    public void A_wrong_weekday_for_a_date_of_the_request_is_dropped()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Which day should the invitation name?",
            ["Wednesday, 12 May 2027", "Friday, 12 May 2027", "Wednesday, 19 May 2027"]);

        var question = Assert.Single(Run(gap, "Write an invitation for 12.5.2027 or 19.5.2027.").Questions);

        Assert.DoesNotContain(question.Options, o => o.Label.StartsWith("Friday", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("de", "Wie lange ist das her?", "1 bis 3 Jahre|3 bis 5 Jahre|Über 5 Jahre")]
    [InlineData("fr", "Depuis combien de temps ?", "Entre 1 et 3 ans|Plus de 3 ans")]
    [InlineData("en", "Should the letter be polite or direct?", "You may be direct|Polite and soft")]
    [InlineData("de", "Wie lang soll der Text werden?", "Kurz, etwa 1 Seite|Länger, 2 bis 3 Seiten")]
    [InlineData("de", "Wann im Jahr soll das Fest sein?", "Im Mai|Im September")]
    public void Durations_amounts_and_month_names_alone_stay(string language, string text, string options)
    {
        var gap = QuestionGateTests.Ask(GapTopic.Facts, text, options.Split('|'));

        var result = Run(gap, "Schreib einen kurzen Text.", language);

        Assert.DoesNotContain(result.Trace, t => t.Decision == GateDecision.DroppedGuessedDate);
        Assert.Equal(Run(gap, "Schreib einen kurzen Text.", language, drop: false).Questions, result.Questions, QuestionComparer.Instance);
    }

    [Fact]
    public void The_rule_never_lets_a_question_through_that_the_form_rules_drop()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Wann soll das Treffen sein?",
            ["Am 12. Mai", "Am 19. Mai", "Am 26. Mai", "Am 2. Juni", "Samstag, 9. Juni"]);
        const string request = "Schreib eine Einladung für den 12. Mai, 19. Mai, 26. Mai oder 2. Juni.";

        Assert.Empty(Run(gap, request, "de", drop: false).Questions);
        var result = Run(gap, request, "de");

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedBadOptions, Assert.Single(result.Trace).Decision);
    }

    [Fact]
    public void Template_length_questions_are_not_touched()
    {
        var gap = new IntakeGap(GapTopic.Length, GapResolution.Ask, true, false, GapImpact.High, "kurz", AssumptionConfidence.Unsure, null);

        var question = Assert.Single(Run(gap, "Schreib eine Einladung.", "de").Questions);

        Assert.True(question.FromTemplate);
    }

    [Fact]
    public void The_switch_is_off_by_default_until_the_gate_is_measured()
    {
        Assert.False(new IntakeOptions().DropGuessedDateOptions);
        Assert.False(new QuestionGateInput(QuestionGateTests.Output([]), "x", false, new Dictionary<GapTopic, MemoryAnswer>(), 3).DropGuessedDates);
    }

    private sealed class QuestionComparer : IEqualityComparer<ClarifyQuestion>
    {
        public static readonly QuestionComparer Instance = new();

        public bool Equals(ClarifyQuestion? x, ClarifyQuestion? y) =>
            x is not null && y is not null && x.Text == y.Text && x.DefaultOptionId == y.DefaultOptionId
            && x.Options.Select(o => (o.Id, o.Label)).SequenceEqual(y.Options.Select(o => (o.Id, o.Label)));

        public int GetHashCode(ClarifyQuestion obj) => obj.Text.GetHashCode(StringComparison.Ordinal);
    }
}
