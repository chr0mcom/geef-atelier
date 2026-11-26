using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Tests.Domain.Intake;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <see cref="AboutMeTopics"/>: which of sender, signature, form of address and language a result had to assume or leave open while About
/// me has nothing on them, and what came from About me. Pure code, no model: the trial run of package 4 (the mapping topic → field is
/// reached without any call, and once a field is saved the reading of the next request takes that topic from About me and assumes
/// nothing about it).
/// </summary>
public sealed class AboutMeTopicsTests
{
    private const string Request = "Schreib eine kurze E-Mail an die Hausverwaltung: Die Heizung ist kalt, bitte um einen Termin.";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<GapTopic, MemoryAnswer> Nothing = new Dictionary<GapTopic, MemoryAnswer>();

    private static IntakeGap[] LetterGaps() =>
    [
        QuestionGateTests.Gap(GapTopic.Signature, GapResolution.Assume, "Unterschrift: [Ihr Name]", AssumptionConfidence.Unsure),
        QuestionGateTests.Gap(GapTopic.Facts, GapResolution.Assume, "Die Wohnung liegt im zweiten Stock", AssumptionConfidence.Unsure),
        QuestionGateTests.Gap(GapTopic.Sender, GapResolution.Memory, "Absender: Mieterin oder Mieter der Wohnung"),
        QuestionGateTests.Gap(GapTopic.Formality, GapResolution.Assume, "Förmlich mit Sie"),
        QuestionGateTests.Gap(GapTopic.Tone, GapResolution.Assume, "Freundlich und sachlich"),
    ];

    private static IntakeRecord Record(QuestionGateResult gate, ClarifierOutput output) =>
        new(IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, "m", 1, null, output, gate.Trace, gate.Questions, gate.Assumptions,
            gate.FromMemory, gate.ResearchTopics, null, BriefMode.AppendAnswers, gate.HeldBack);

    private static (IntakeRecord Record, QuestionGateResult Gate) Read(UserProfileEntity? profile, params IntakeGap[] gaps)
    {
        var output = QuestionGateTests.Output(gaps);
        var gate = QuestionGate.Apply(new QuestionGateInput(output, Request, false, ProfileMemoryResolution.Answers(profile), 3));
        return (Record(gate, output), gate);
    }

    private static UserProfileEntity Profile() => new() { Username = "erika", CreatedAt = T0, UpdatedAt = T0 };

    [Fact]
    public void Without_about_me_the_assumed_topics_are_offered_in_fixed_order_and_facts_or_tone_never()
    {
        var (record, _) = Read(null, LetterGaps());

        Assert.Equal([GapTopic.Sender, GapTopic.Signature, GapTopic.Formality], AboutMeTopics.Missing(record, [], null));
        Assert.Equal(AboutMeTopics.Missing(record, [], null), AboutMeTopics.Missing(record, [], Profile()));
    }

    [Fact]
    public void Once_saved_the_next_request_takes_the_topic_from_about_me_and_assumes_nothing_about_it()
    {
        var saved = Profile() with { DisplayName = "Erika Beispiel", Signature = "Viele Grüße\nErika Beispiel" };

        var (before, gateBefore) = Read(null, LetterGaps());
        var (after, gateAfter) = Read(saved, LetterGaps());

        Assert.Contains(gateBefore.Assumptions, a => a.Topic == GapTopic.Signature);
        Assert.Contains(gateBefore.Assumptions, a => a.Topic == GapTopic.Sender);
        Assert.DoesNotContain(gateAfter.Assumptions, a => a.Topic is GapTopic.Signature or GapTopic.Sender);
        Assert.Equal([GapTopic.Sender, GapTopic.Signature], gateAfter.FromMemory.Select(m => m.Topic).Order());
        Assert.Equal("Viele Grüße\nErika Beispiel", gateAfter.FromMemory.Single(m => m.Topic == GapTopic.Signature).Value);
        Assert.Contains(gateAfter.Assumptions, a => a.Topic == GapTopic.Facts);
        Assert.Equal([GapTopic.Formality], AboutMeTopics.Missing(after, [], saved));
        Assert.Equal([GapTopic.Formality], AboutMeTopics.Missing(before, [], saved));
        Assert.Equal([GapTopic.Sender, GapTopic.Signature], AboutMeTopics.FromAboutMe(null, after));
    }

    [Fact]
    public void A_sender_answered_by_role_or_organization_alone_is_known_too()
    {
        var (record, _) = Read(null, LetterGaps());

        Assert.DoesNotContain(GapTopic.Sender, AboutMeTopics.Missing(record, [], Profile() with { Organization = "Beispiel GmbH" }));
        Assert.DoesNotContain(GapTopic.Formality, AboutMeTopics.Missing(record, [], Profile() with { Formality = Formality.Formal }));
        Assert.Contains(GapTopic.Formality, AboutMeTopics.Missing(record, [], Profile() with { Formality = Formality.Unset, Language = "de" }));
    }

    [Fact]
    public void A_corrected_assumption_is_not_offered_and_a_question_left_open_is()
    {
        var question = QuestionGateTests.Ask(GapTopic.Language, "Auf Deutsch oder Englisch?", ["Deutsch", "Englisch"], assumption: null);
        var (record, gate) = Read(null, [QuestionGateTests.Gap(GapTopic.Signature, GapResolution.Assume, "Unterschrift: [Ihr Name]", AssumptionConfidence.Unsure), question]);
        var signature = gate.Assumptions.Single(a => a.Topic == GapTopic.Signature);
        var asked = gate.Questions.Single();

        Assert.Equal([GapTopic.Signature, GapTopic.Language], AboutMeTopics.Missing(record, [], null));
        Assert.Equal([GapTopic.Language], AboutMeTopics.Missing(record, [new OrderAnswer(signature.Id, null, "Ihre Erika", false, AnswerOrigin.User)], null));
        Assert.Equal([GapTopic.Signature], AboutMeTopics.Missing(record, [new OrderAnswer(asked.Id, "o1", null, false, AnswerOrigin.User)], null));
        Assert.Equal([GapTopic.Signature, GapTopic.Language], AboutMeTopics.Missing(record, [new OrderAnswer(asked.Id, null, null, true, AnswerOrigin.User)], null));
    }

    [Fact]
    public void Without_a_reading_of_the_request_nothing_is_offered()
    {
        Assert.Empty(AboutMeTopics.Missing(null, [], null));
        Assert.Empty(AboutMeTopics.Missing(IntakeRecord.Fallback(IntakeRecordStatus.TimedOut, "timeout", 1, null, BriefMode.AppendAnswers), [], null));
        var (record, _) = Read(null, QuestionGateTests.Gap(GapTopic.Tone, GapResolution.Assume, "Freundlich"), QuestionGateTests.Gap(GapTopic.Facts, GapResolution.Assume, "Zweiter Stock"));
        Assert.Empty(AboutMeTopics.Missing(record, [], null));
    }

    [Fact]
    public void What_came_from_about_me_is_read_from_the_profile_block_of_the_briefing()
    {
        var profile = Profile() with { Role = "Kassenwart", Language = "de", Signature = "Name: Erika\nRole: keine", NoGos = "Keine Emojis" };
        var briefing = Request + BriefingSections.Separator + BriefingSections.ProfileHeading + "\n" + ProfilePrompt.Render(profile);

        Assert.Equal([GapTopic.Sender, GapTopic.Signature, GapTopic.Language], AboutMeTopics.FromAboutMe(briefing, null));
        Assert.Empty(AboutMeTopics.FromAboutMe(Request, null));
        Assert.Empty(AboutMeTopics.FromAboutMe(Request + BriefingSections.Separator + BriefingSections.ProfileHeading + "\n" + ProfilePrompt.Render(Profile() with { NoGos = "Keine Emojis" }), null));
        Assert.Empty(AboutMeTopics.FromAboutMe(null, null));
    }
}
