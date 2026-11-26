using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The block of the questions left open is a block of the composer: research and the web page get only the request in front of it, the
/// questions come back word for word, a change keeps the block and adds its own line after it, and only the full heading opens the
/// block. The example is made up.
/// </summary>
public sealed class OpenQuestionsParseTests
{
    private const string Request = "Antworte der Schule auf den Elternbrief zur Klassenfahrt im Anhang.";
    private const string Question = "Darf dein Kind an der Klassenfahrt teilnehmen?";

    private static readonly string Briefing =
        Request + BriefingSections.Separator +
        IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n- Soll ein Rückruf angeboten werden?\n" +
        IntakeBriefComposer.AssumptionsHeading + "\n- Förmliches Sie." +
        "\n\n" + IntakeBriefComposer.ProfileHeading + "\nName: Anna Beispiel";

    [Fact]
    public void Research_gets_only_the_request_when_the_open_questions_come_first()
    {
        Assert.Equal(Request, BriefingSections.RequestPart(Briefing));
    }

    [Fact]
    public void The_open_questions_come_back_word_for_word_and_are_no_assumptions_or_answers()
    {
        var parts = BriefingSections.Parse(Briefing);

        Assert.Equal(Request, parts.Request);
        Assert.Equal([Question, "Soll ein Rückruf angeboten werden?"], parts.OpenQuestions!);
        Assert.Equal(["Förmliches Sie."], parts.FormAssumptions);
        Assert.Empty(parts.PersonAnswers);
        Assert.Empty(parts.AssistantAnswers);
        Assert.Equal("Name: Anna Beispiel", parts.Profile);
    }

    [Fact]
    public void A_briefing_without_the_block_has_no_open_questions()
    {
        Assert.Empty(BriefingSections.Parse(Request + BriefingSections.Separator + IntakeBriefComposer.AssumptionsHeading + "\n- Förmliches Sie.").OpenQuestions!);
        Assert.Empty(BriefingSections.Parse(Request).OpenQuestions!);
    }

    [Fact]
    public void A_request_that_only_starts_like_the_heading_stays_the_request()
    {
        var request = "Schreib einen Aushang.\n\n---\nQuestions the person did not answer\n- Wer räumt auf?";

        Assert.Equal(request, BriefingSections.RequestPart(request));
        Assert.Empty(BriefingSections.Parse(request).OpenQuestions!);
        var notChecked = "Schreib einen Aushang.\n\n---\nThe request was not checked for missing details before writing.";
        Assert.Equal(notChecked, BriefingSections.RequestPart(notChecked));
    }

    [Fact]
    public void Inside_the_blocks_only_the_full_heading_starts_the_open_questions()
    {
        var briefing = Request + BriefingSections.Separator + IntakeBriefComposer.AssumptionsHeading + "\n- Förmliches Sie.\n" +
                       "Questions the person did not answer\n- Wer räumt auf?";

        var parts = BriefingSections.Parse(briefing);

        Assert.Empty(parts.OpenQuestions!);
        Assert.Equal(["Förmliches Sie.", "Wer räumt auf?"], parts.FormAssumptions);
    }

    [Fact]
    public void A_change_keeps_the_open_questions_and_the_person_can_settle_them()
    {
        var changed = RevisionBrief.Compose(Briefing, "Bitte schreib, dass es mitfährt.", 2);

        Assert.StartsWith(Briefing[..Briefing.IndexOf("\n\n" + IntakeBriefComposer.ProfileHeading, StringComparison.Ordinal)], changed, StringComparison.Ordinal);
        var parts = BriefingSections.Parse(changed);
        Assert.Equal([Question, "Soll ein Rückruf angeboten werden?"], parts.OpenQuestions!);
        Assert.Equal("Bitte schreib, dass es mitfährt.", Assert.Single(parts.Changes).Words);
        Assert.Equal(Request, BriefingSections.RequestPart(changed));
    }
}
