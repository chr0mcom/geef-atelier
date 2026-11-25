using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// An option that only promises to hand the information over later is no answer. The examples come from the development set of
/// the measurement.
/// </summary>
public sealed class QuestionGateDeferralOptionTests
{
    [Theory]
    [InlineData("Ich füge sie gleich hier ein")]
    [InlineData("Ich lade sie als Datei hoch")]
    [InlineData("Ich hänge sie als Datei an")]
    [InlineData("Ich tippe sie kurz ab")]
    [InlineData("Ich schreibe sie in den Chat")]
    [InlineData("Ich nenne Datum, Uhrzeit und Ort")]
    [InlineData("Nummer, Betrag und Datum nenne ich gleich")]
    [InlineData("Platzhalter zum Ausfüllen einsetzen")]
    [InlineData("Platzhalter einfügen, ich fülle sie selbst aus")]
    [InlineData("I'll type the date, time and place")]
    [InlineData("Saturday, I'll add time and place")]
    [InlineData("I’ll paste my notes")]
    [InlineData("I'll list the key points and tasks")]
    [InlineData("Yes, I'll add a short memory")]
    [InlineData("Use a placeholder like [Your Name]")]
    [InlineData("Je vais donner mes propres détails")]
    public void A_promise_to_add_it_later_is_no_answer(string option) => Assert.True(JargonBlocklist.IsDeferralOption(option), option);

    [Theory]
    [InlineData("Wir duzen uns")]
    [InlineData("Ich weiß es nicht genau, will nur Belege sehen")]
    [InlineData("Anmeldung bei mir nötig")]
    [InlineData("Noch nichts, nur Entschuldigung")]
    [InlineData("Link kommt später per Mail")]
    [InlineData("Je lui laisse choisir")]
    [InlineData("Let her choose replacement or refund")]
    [InlineData("Also mention cooking, homework help or tutoring")]
    [InlineData("Nachfüllen und Pflege")]
    [InlineData("Ersatz des beschädigten Artikels")]
    [InlineData("Not fixed yet")]
    [InlineData("Ich schreibe als Mieter")]
    [InlineData("Ich trage die Kosten")]
    [InlineData("Ich gebe die Wohnung zurück")]
    [InlineData("I'll give a full refund")]
    [InlineData("I will write on behalf of the team")]
    [InlineData("Je donne ma démission")]
    [InlineData("No placeholders")]
    [InlineData("Ohne Platzhalter")]
    public void A_real_answer_stays(string option) => Assert.False(JargonBlocklist.IsDeferralOption(option), option);

    [Fact]
    public void A_question_with_only_such_options_is_not_asked()
    {
        var result = Run(QuestionGateTests.Ask(GapTopic.Facts, "Wie kommen die Stichpunkte zu mir?",
            ["Ich füge sie jetzt hier ein", "Ich lade sie als Datei hoch"]));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedBadOptions, Assert.Single(result.Trace).Decision);
    }

    [Fact]
    public void The_real_options_stay_and_the_default_follows_its_option()
    {
        var gap = QuestionGateTests.Ask(GapTopic.Facts, "Was soll mit dem Sofa passieren?",
            ["Ich schreibe es gleich dazu", "Ersatz liefern", "Geld zurück"]);
        gap = gap with { Question = gap.Question! with { DefaultIndex = 2 } };

        var question = Assert.Single(Run(gap).Questions);

        Assert.Equal(["Ersatz liefern", "Geld zurück"], question.Options.Select(o => o.Label));
        Assert.Equal(question.Options[1].Id, question.DefaultOptionId);
    }

    private static QuestionGateResult Run(IntakeGap gap) => QuestionGate.Apply(new QuestionGateInput(
        QuestionGateTests.Output([gap], "de"), "Schreib eine Reklamation wegen des Sofas.", false,
        new Dictionary<GapTopic, MemoryAnswer>(), 3));
}
