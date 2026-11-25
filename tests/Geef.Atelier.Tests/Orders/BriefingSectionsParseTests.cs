using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <see cref="BriefingSections.Parse"/>: every briefing the composer builds splits back into request, answers of the person and of the
/// assistant, form assumptions, open facts and profile; a briefing not built by the composer is all request.
/// </summary>
public sealed class BriefingSectionsParseTests
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.\n\n---\nBitte mit Anmeldung.\n- Punkt im Auftrag";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserProfileEntity Profile = new()
    {
        Username = "user-a", DisplayName = "Anna Beispiel", Signature = "Viele Grüße\nAnna", NoGos = "Keine Emojis", CreatedAt = T0, UpdatedAt = T0,
    };

    public static TheoryData<BriefMode, ContentAssumptionMode, bool, int, bool> Combinations()
    {
        var data = new TheoryData<BriefMode, ContentAssumptionMode, bool, int, bool>();
        foreach (var mode in Enum.GetValues<BriefMode>())
            foreach (var content in Enum.GetValues<ContentAssumptionMode>())
                foreach (var shown in new[] { false, true })
                    foreach (var answers in new[] { 0, 1, 2 })
                        foreach (var profile in new[] { false, true })
                            data.Add(mode, content, shown, answers, profile);
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_composed_briefing_splits_back_into_its_parts(BriefMode mode, ContentAssumptionMode content, bool shown, int answerSet, bool withProfile)
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions { BriefMode = mode, ContentAssumptions = content }));
        IReadOnlyList<OrderAnswer> answers = answerSet switch
        {
            1 => [new("length", "short", null, false, AnswerOrigin.User), new("q1", "o2", "Bitte mit Grill", false, AnswerOrigin.Client)],
            2 =>
            [
                new("length", "short", null, false, AnswerOrigin.User),
                new("q1", "o2", "Bitte mit Grill", false, AnswerOrigin.Client),
                new("a1", null, "Eher förmlich", false, AnswerOrigin.User),
                new("a2", null, "Am 12. Juli", false, AnswerOrigin.Client),
            ],
            _ => [],
        };

        var brief = composer.Compose(new OrderBriefInput(Order("user-a", Request), IntakeBriefComposerTests.Record(), answers, withProfile ? Profile : null, shown));
        var parts = BriefingSections.Parse(brief.Text);

        Assert.Equal(Request, parts.Request);
        Assert.Equal(answerSet switch
        {
            0 => [],
            1 => ["Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)"],
            _ => ["Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)", "Eher förmlich"],
        }, parts.PersonAnswers);
        Assert.Equal(answerSet switch
        {
            0 => [],
            1 => ["Wer kommt? → Ganzes Team – \"Bitte mit Grill\""],
            _ => ["Wer kommt? → Ganzes Team – \"Bitte mit Grill\"", "Am 12. Juli"],
        }, parts.AssistantAnswers);

        IReadOnlyList<string> form = answerSet == 2 ? [] : content == ContentAssumptionMode.Defaults ? ["Lockerer, herzlicher Ton", "Datum als Platzhalter"] : ["Lockerer, herzlicher Ton"];
        IReadOnlyList<string> open = answerSet == 2 || content == ContentAssumptionMode.Defaults ? [] : ["Datum als Platzhalter"];
        Assert.Equal(form, parts.FormAssumptions);
        Assert.Equal(open, parts.OpenFacts);
        Assert.Equal(withProfile ? ProfilePrompt.Render(Profile) : null, parts.Profile);
        Assert.DoesNotContain(parts.PersonAnswers.Concat(parts.AssistantAnswers), a => a.Contains("Lockerer", StringComparison.Ordinal) || a.Contains("Platzhalter", StringComparison.Ordinal));
        Assert.DoesNotContain(parts.PersonAnswers.Concat(parts.AssistantAnswers), a => a.Contains("Einladung zum Sommerfest des Teams", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnswerOrigin.User, true)]
    [InlineData(AnswerOrigin.Client, false)]
    public void The_revision_answer_belongs_to_whoever_gave_it(AnswerOrigin origin, bool byPerson)
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var record = IntakeBriefComposerTests.Record() with { RevisionSourceId = Guid.NewGuid(), Assumptions = [] };

        var parts = BriefingSections.Parse(composer.Compose(new OrderBriefInput(Order("user-a", Request), record, [new("revision", "no", null, false, origin)], null)).Text);

        var line = IntakeBriefComposer.RevisionQuestion + " → " + IntakeBriefComposer.RevisionNo;
        Assert.Equal(byPerson ? [line] : [], parts.PersonAnswers);
        Assert.Equal(byPerson ? [] : [line], parts.AssistantAnswers);
    }

    [Fact]
    public void Only_the_profile_is_the_profile_even_when_it_has_lines_like_answers()
    {
        var profile = new UserProfileEntity { Username = "user-a", FreeText = "- " + IntakeBriefComposer.AnswersHeading + "\n- Keine Fotos erwähnen", CreatedAt = T0, UpdatedAt = T0 };
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));

        var parts = BriefingSections.Parse(composer.Compose(new OrderBriefInput(Order("user-a", Request), null, [], profile)).Text);

        Assert.Equal(Request, parts.Request);
        Assert.Equal(ProfilePrompt.Render(profile), parts.Profile);
        Assert.Empty(parts.PersonAnswers);
        Assert.Empty(parts.FormAssumptions);
    }

    [Theory]
    [InlineData("Compose a crew for the following task:\n\nSchreib etwas.\n\n---\nNoch ein Absatz.")]
    [InlineData("Schreib etwas.")]
    [InlineData("")]
    public void A_briefing_not_built_by_the_composer_is_all_request(string briefing)
    {
        var parts = BriefingSections.Parse(briefing);

        Assert.Equal(briefing, parts.Request);
        Assert.Empty(parts.PersonAnswers);
        Assert.Empty(parts.AssistantAnswers);
        Assert.Empty(parts.FormAssumptions);
        Assert.Empty(parts.OpenFacts);
        Assert.Null(parts.Profile);
    }

    [Fact]
    public void A_briefing_with_only_open_facts_still_ends_the_request_before_them()
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var record = IntakeBriefComposerTests.Record() with { Assumptions = [new IntakeAssumption("a1", GapTopic.Facts, "Fotos liegen bei", AssumptionConfidence.Unsure)] };

        var text = composer.Compose(new OrderBriefInput(Order("user-a", Request), record, [], null)).Text;

        Assert.Equal(Request, BriefingSections.RequestPart(text));
        Assert.Equal(["Fotos liegen bei"], BriefingSections.Parse(text).OpenFacts);
        Assert.Empty(BriefingSections.Parse(text).FormAssumptions);
    }
}
