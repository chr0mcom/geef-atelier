using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <see cref="RevisionBrief"/>: the change block sits before the About me block in every composed briefing, older changes stay, the
/// request part never carries a change, and <see cref="BriefingSections.Parse"/> reads the changes as words of the person.
/// </summary>
public sealed class RevisionBriefTests
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.\n\n---\nBitte mit Anmeldung.";
    private const string Marker = "PROFIL-MARKER-N1";
    private const string Change2 = "The meeting is on 5 May, not 12 May.";
    private const string Change3 = "Make the text about a third shorter.";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserProfileEntity Profile = new()
    {
        Username = "user-a", DisplayName = "Anna Beispiel", FreeText = Marker, CreatedAt = T0, UpdatedAt = T0,
    };

    public static TheoryData<BriefMode, bool, bool, bool, ContentAssumptionMode> Combinations()
    {
        var data = new TheoryData<BriefMode, bool, bool, bool, ContentAssumptionMode>();
        foreach (var mode in Enum.GetValues<BriefMode>())
            foreach (var record in new[] { false, true })
                foreach (var answers in new[] { false, true })
                    foreach (var profile in new[] { false, true })
                        foreach (var content in Enum.GetValues<ContentAssumptionMode>())
                            data.Add(mode, record, answers, profile, content);
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void In_every_composed_briefing_the_changes_come_before_the_profile_and_the_rest_stays(
        BriefMode mode, bool withRecord, bool withAnswers, bool withProfile, ContentAssumptionMode content)
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions { BriefMode = mode, ContentAssumptions = content }));
        IReadOnlyList<OrderAnswer> answers = withAnswers
            ? [new("length", "short", null, false, AnswerOrigin.User), new("a1", null, "Eher förmlich", false, AnswerOrigin.Client)]
            : [];
        var original = composer.Compose(new OrderBriefInput(Order("user-a", Request), withRecord ? IntakeBriefComposerTests.Record() : null, answers,
            withProfile ? Profile : null, true)).Text;
        var before = BriefingSections.Parse(original);

        var v2 = RevisionBrief.Compose(original, Change2, 2);
        var v3 = RevisionBrief.Compose(v2, Change3, 3);

        foreach (var brief in new[] { v2, v3 })
        {
            Assert.Equal(Request, BriefingSections.RequestPart(brief));
            var parts = BriefingSections.Parse(brief);
            Assert.Equal(before.Profile, parts.Profile);
            Assert.Equal(before.AssistantAnswers, parts.AssistantAnswers);
            Assert.Equal(before.FormAssumptions, parts.FormAssumptions);
            Assert.Equal(before.OpenFacts, parts.OpenFacts);
            Assert.DoesNotContain(Marker, BriefingSections.WithoutProfile(brief), StringComparison.Ordinal);
            Assert.Contains(RevisionBrief.Heading, BriefingSections.WithoutProfile(brief), StringComparison.Ordinal);
            Assert.Equal(withProfile, BriefingSections.WithoutProfile(brief).EndsWith(BriefingSections.ProfileHiddenNote, StringComparison.Ordinal));
            if (withProfile)
                Assert.True(brief.IndexOf(RevisionBrief.Heading, StringComparison.Ordinal) < brief.IndexOf(BriefingSections.ProfileHeading, StringComparison.Ordinal));
        }

        Assert.Equal(before.PersonAnswers, BriefingSections.Parse(v2).PersonAnswers);
        Assert.Equal(before.PersonAnswers, BriefingSections.Parse(v3).PersonAnswers);
        Assert.Equal([new FactChange(2, Change2, Newest: true)], BriefingSections.Parse(v2).Changes);
        Assert.Equal([new FactChange(2, Change2, Newest: false), new FactChange(3, Change3, Newest: true)], BriefingSections.Parse(v3).Changes);
        Assert.Equal(["(earlier change, version 2; a newer change wins where they differ) " + Change2, "(newest change, version 3) " + Change3],
            BriefingSections.Parse(v3).Changes.Select(c => c.Marked));
        Assert.Single(AllIndexes(v3, RevisionBrief.Heading));
        Assert.Contains(RevisionBrief.Heading + "\n" + Earlier(2, Change2) + "\n" + Now(3, Change3), v3, StringComparison.Ordinal);
        Assert.StartsWith(original.Contains(BriefingSections.ProfileHeading, StringComparison.Ordinal)
            ? original[..original.IndexOf(BriefingSections.ProfileHeading, StringComparison.Ordinal)].TrimEnd('\n')
            : original, v2, StringComparison.Ordinal);
    }

    [Fact]
    public void A_briefing_that_is_only_the_request_gains_the_block_after_the_separator()
    {
        const string request = "Write a short note to the team.";

        var brief = RevisionBrief.Compose(request, Change2, 2);

        Assert.Equal(request + BriefingSections.Separator + RevisionBrief.Heading + "\n" + Now(2, Change2), brief);
        Assert.Equal(request, BriefingSections.RequestPart(brief));
        Assert.Empty(BriefingSections.Parse(brief).PersonAnswers);
        Assert.Equal([new FactChange(2, Change2, Newest: true)], BriefingSections.Parse(brief).Changes);
        Assert.Equal(brief, BriefingSections.WithoutProfile(brief));
    }

    [Fact]
    public void With_only_a_profile_the_block_goes_between_separator_and_profile()
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var original = composer.Compose(new OrderBriefInput(Order("user-a", "Write a note."), null, [], Profile)).Text;
        var profileBlock = original[(original.IndexOf(BriefingSections.Separator, StringComparison.Ordinal) + BriefingSections.Separator.Length)..];

        var brief = RevisionBrief.Compose(original, Change2, 2);

        Assert.Equal("Write a note." + BriefingSections.Separator + RevisionBrief.Heading + "\n" + Now(2, Change2) + "\n\n" + profileBlock, brief);
        Assert.Equal("Write a note." + BriefingSections.Separator + RevisionBrief.Heading + "\n" + Now(2, Change2) + "\n\n" + BriefingSections.ProfileHiddenNote,
            BriefingSections.WithoutProfile(brief));
    }

    [Fact]
    public void A_change_with_line_breaks_stays_on_one_line_and_an_empty_change_is_refused()
    {
        var brief = RevisionBrief.Compose("Write a note.", "  First line.\n\nSecond   line.\r\n", 2);

        Assert.EndsWith(Now(2, "First line. Second line."), brief, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => RevisionBrief.Compose("Write a note.", " \n ", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => RevisionBrief.Compose("Write a note.", Change2, 0));
    }

    [Fact]
    public void A_request_with_its_own_separator_keeps_its_request_part()
    {
        var request = "Write a note.\n\n---\n" + RevisionBrief.Heading.ToUpperInvariant();

        var brief = RevisionBrief.Compose(request, Change2, 2);

        Assert.Equal(request, BriefingSections.RequestPart(brief));
    }

    [Fact]
    public void The_change_joins_the_words_and_the_quick_sentences_once_each()
    {
        Assert.Equal(Change2, RevisionBrief.Change("  " + Change2 + "\n", []));
        Assert.Equal("Fix the date. Make the text about a third shorter. Use simpler words and shorter sentences.",
            RevisionBrief.Change("Fix\nthe date.", [RevisionQuick.Shorter, RevisionQuick.Simpler, RevisionQuick.Shorter]));
        Assert.Equal("Make the tone more formal.", RevisionBrief.Change(null, [RevisionQuick.MoreFormal]));
        Assert.Equal(string.Empty, RevisionBrief.Change(" \n ", []));
    }

    [Theory]
    [InlineData("The meeting is on 5 May, not 12 May.", new RevisionQuick[0], "The meeting is on 5 May, not 12 May.")]
    [InlineData(null, new[] { RevisionQuick.Shorter }, "Shorter")]
    [InlineData(null, new[] { RevisionQuick.Friendlier, RevisionQuick.Simpler }, "Friendlier, Simpler words")]
    [InlineData("Add the room number.", new[] { RevisionQuick.Longer, RevisionQuick.MoreFormal }, "Add the room number. (Longer, More formal)")]
    public void What_was_asked_is_read_back_as_the_words_and_the_button_names(string? words, RevisionQuick[] quick, string expected)
    {
        Assert.Equal(expected, RevisionBrief.Requested(RevisionBrief.Change(words, quick)));
    }

    [Fact]
    public void Without_a_change_nothing_was_asked()
    {
        Assert.Null(RevisionBrief.Requested(null));
        Assert.Null(RevisionBrief.Requested("  "));
        Assert.Equal("Please rewrite: Make the tone more formal.x", RevisionBrief.Requested("Please rewrite: Make the tone more formal.x"));
    }

    [Fact]
    public void Only_the_newest_change_is_the_one_to_make_now_and_every_earlier_one_is_marked_as_already_made()
    {
        var v2 = RevisionBrief.Compose("Write a note.", "Make the text about a third shorter.", 2);
        var v3 = RevisionBrief.Compose(v2, "Add the room number.", 3);
        var v4 = RevisionBrief.Compose(v3, "The meeting is on 5 May.", 4);

        Assert.Equal("Write a note." + BriefingSections.Separator + RevisionBrief.Heading + "\n"
            + "- (version 2, earlier change: already made in the text – do not make it again, keep it) Make the text about a third shorter.\n"
            + "- (version 3, earlier change: already made in the text – do not make it again, keep it) Add the room number.\n"
            + "- (version 4, the change to make now) The meeting is on 5 May.", v4);
        Assert.Single(AllIndexes(v4, "the change to make now"));
    }

    [Fact]
    public void The_heading_says_that_the_changes_take_precedence_over_the_assumptions_and_the_facts_not_given()
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions { BriefMode = BriefMode.AppendAnswers, ContentAssumptions = ContentAssumptionMode.OpenPoints }));
        var original = composer.Compose(new OrderBriefInput(Order("user-a", Request), IntakeBriefComposerTests.Record(), [], Profile, true)).Text;

        var brief = RevisionBrief.Compose(original, Change2, 2);

        Assert.Contains("takes precedence over the blocks \"Assumptions …\" and \"Not given by the person\"", RevisionBrief.Heading, StringComparison.Ordinal);
        var heading = brief.IndexOf(RevisionBrief.Heading, StringComparison.Ordinal);
        Assert.True(heading > brief.IndexOf(IntakeBriefComposer.AssumptionsHeadingStart, StringComparison.Ordinal));
        Assert.True(heading > brief.IndexOf(IntakeBriefComposer.OpenFactsHeadingStart, StringComparison.Ordinal));
    }

    [Fact]
    public void The_words_of_the_person_stay_unchanged_after_the_mark_also_when_they_start_with_brackets()
    {
        const string words = "(version 9) keep this bracket. The meeting is on 5 May.";
        var v3 = RevisionBrief.Compose(RevisionBrief.Compose("Write a note.", words, 2), Change3, 3);

        var changes = BriefingSections.Parse(v3).Changes;

        Assert.Equal([new FactChange(2, words, Newest: false), new FactChange(3, Change3, Newest: true)], changes);
        Assert.Equal("(earlier change, version 2; a newer change wins where they differ) " + words, changes[0].Marked);
    }

    private static string Now(int version, string change) => $"- (version {version}, the change to make now) {change}";

    private static string Earlier(int version, string change) => $"- (version {version}, earlier change: already made in the text – do not make it again, keep it) {change}";

    private static List<int> AllIndexes(string text, string value)
    {
        var found = new List<int>();
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            found.Add(i);
        return found;
    }
}
