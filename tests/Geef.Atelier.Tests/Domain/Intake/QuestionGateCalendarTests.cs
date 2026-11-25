using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// With <see cref="QuestionGateInput.DropGuessedDates"/> the gate drops clickable answers naming a calendar day only when nothing in
/// the material of the person (request, file texts, About me details, known answers) looks like a date; then fewer than two left turn
/// the question into its assumption (<see cref="GateDecision.DroppedBadOptions"/>). As soon as anything date-like stands there, every
/// option stays, also an invented one: that is the bound of the rule. Durations and amounts never count. Without the switch the gate
/// is unchanged.
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
        IReadOnlyList<string>? material = null,
        bool drop = true,
        IReadOnlyDictionary<GapTopic, MemoryAnswer>? known = null) =>
        QuestionGate.Apply(new QuestionGateInput(
            QuestionGateTests.Output([gap], language), request, false, known ?? new Dictionary<GapTopic, MemoryAnswer>(), 3, material, drop));

    [Fact]
    public void Guessed_reopening_days_of_an_order_without_any_date_are_not_offered_and_the_assumption_stays_visible()
    {
        var result = Run(ReopenGap(), ClosedOverHolidays);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedBadOptions, Assert.Single(result.Trace).Decision);
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
    public void Only_the_options_with_a_day_fall_and_the_default_follows()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "When should the market take place?",
            ["On a weekend", "Saturday, 26 May", "In June"], defaultIndex: 2);

        var question = Assert.Single(Run(gap, "Write a notice for our flea market.").Questions);

        Assert.Equal(["On a weekend", "In June"], question.Options.Select(o => o.Label));
        Assert.Equal(question.Options[1].Id, question.DefaultOptionId);
    }

    [Theory]
    [InlineData("Schreib einen Aushang für den Elternabend am 12. und 13. Mai.", "12. Mai|13. Mai")]
    [InlineData("Schreib eine Einladung, Elternabend am 3. oder 4. Mai.", "3. Mai|4. Mai")]
    [InlineData("Write an invitation, we are open May 5-7 or May 12-14.", "May 5 to May 7|May 12 to May 14")]
    [InlineData("Schreib eine Erinnerung: Anmeldung bis 12.05 oder 19.05 möglich.", "12. Mai|19. Mai")]
    [InlineData("Schreib eine Einladung für Samstagnachmittag oder Sonntagvormittag.", "Samstag|Sonntag")]
    [InlineData("Schreib eine Einladung, am Sa, 6.6. oder So, 7.6.", "Samstag, 6. Juni|Sonntag, 7. Juni")]
    [InlineData("Schreib eine Einladung für den 12. Dez.", "Freitag, 12. Dezember|Samstag, 13. Dezember")]
    [InlineData("Schreib eine Einladung zum Fest im Mai.", "5. Mai|12. Mai")]
    [InlineData("Write a letter about the meeting on June 5, 2027.", "6/5/2027|5/6/2027")]
    public void Any_date_in_the_request_keeps_every_option(string request, string options)
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, request.StartsWith("Write", StringComparison.Ordinal) ? "Which date should it name?" : "Welcher Termin ist gemeint?",
            options.Split('|'));
        var language = request.StartsWith("Write", StringComparison.Ordinal) ? "en" : "de";

        var question = Assert.Single(Run(gap, request, language).Questions);

        Assert.Equal(options.Split('|'), question.Options.Select(o => o.Label));
    }

    [Fact]
    public void A_date_in_a_file_the_about_me_details_or_a_known_answer_keeps_every_option()
    {
        Assert.Single(Run(ReopenGap(), ClosedOverHolidays, material: ["Notes: back on 04.01."]).Questions);
        Assert.Single(Run(ReopenGap(), ClosedOverHolidays, material: ["About me: office hours Monday to Thursday"]).Questions);
        Assert.Single(Run(ReopenGap(), ClosedOverHolidays, known: new Dictionary<GapTopic, MemoryAnswer>
        {
            [GapTopic.Signature] = new(GapTopic.Signature, "Team office, closed on Fridays", "profile"),
        }).Questions);
    }

    [Fact]
    public void The_bound_of_the_rule_an_invented_date_stays_when_any_date_stands_in_the_material()
    {
        var question = Assert.Single(Run(ReopenGap(), $"{ClosedOverHolidays} The last working day is 23.12.").Questions);

        Assert.Equal(3, question.Options.Count);
    }

    [Theory]
    [InlineData("de", "Wie lange ist das her?", "1 bis 3 Jahre|3 bis 5 Jahre|Über 5 Jahre")]
    [InlineData("fr", "Depuis combien de temps ?", "Entre 1 et 3 ans|Plus de 3 ans")]
    [InlineData("en", "Should the letter be polite or direct?", "You may be direct|Polite and soft")]
    [InlineData("en", "How many people may join?", "Up to 3 may join|More than 3")]
    [InlineData("de", "Wie lang soll der Text werden?", "Kurz, etwa 1 Seite|Länger, 2 bis 3 Seiten")]
    [InlineData("de", "Wann im Jahr soll das Fest sein?", "Im Mai|Im September")]
    public void Durations_amounts_and_month_names_alone_stay(string language, string text, string options)
    {
        var gap = QuestionGateTests.Ask(GapTopic.Facts, text, options.Split('|'));

        var result = Run(gap, "Schreib einen kurzen Text.", language);

        Assert.Equal(Run(gap, "Schreib einen kurzen Text.", language, drop: false).Questions, result.Questions, QuestionComparer.Instance);
        Assert.Equal(options.Split('|'), Assert.Single(result.Questions).Options.Select(o => o.Label));
    }

    [Fact]
    public void The_rule_never_lets_a_question_through_that_the_form_rules_drop()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Date, "Wann soll das Treffen sein?",
            ["Am 12. Mai", "Am 19. Mai", "Am 26. Mai", "Am 2. Juni", "Samstag, 9. Juni"]);
        const string request = "Schreib eine Einladung zum Treffen.";

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
    public void The_switch_is_off_in_code_and_only_the_shipped_settings_turn_it_on()
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
