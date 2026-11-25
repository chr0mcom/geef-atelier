using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="BriefingSections"/>: the request part of every composed briefing, and the profile hidden from anyone but the owner.</summary>
public sealed class BriefingSectionsTests
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.\n\n---\nBitte mit Anmeldung.";
    private const string Marker = "PROFIL-MARKER-K4";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserProfileEntity Profile = new()
    {
        Username = "user-a", DisplayName = "Anna Beispiel", FreeText = Marker, CreatedAt = T0, UpdatedAt = T0,
    };

    public static TheoryData<BriefMode, bool, bool, bool, ContentAssumptionMode, bool> Combinations()
    {
        var data = new TheoryData<BriefMode, bool, bool, bool, ContentAssumptionMode, bool>();
        foreach (var mode in Enum.GetValues<BriefMode>())
            foreach (var record in new[] { false, true })
                foreach (var answers in new[] { false, true })
                    foreach (var profile in new[] { false, true })
                        foreach (var content in Enum.GetValues<ContentAssumptionMode>())
                            foreach (var shown in new[] { false, true })
                                data.Add(mode, record, answers, profile, content, shown);
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void The_request_part_of_every_composed_briefing_is_exactly_the_request(
        BriefMode mode, bool withRecord, bool withAnswers, bool withProfile, ContentAssumptionMode content, bool shown)
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions { BriefMode = mode, ContentAssumptions = content }));
        IReadOnlyList<OrderAnswer> answers = withAnswers
            ? [new("length", "short", null, false, AnswerOrigin.User), new("a1", null, "Eher förmlich", false, AnswerOrigin.Client)]
            : [];
        var brief = composer.Compose(new OrderBriefInput(Order("user-a", Request), withRecord ? IntakeBriefComposerTests.Record() : null, answers,
            withProfile ? Profile : null, shown));

        Assert.Equal(Request, BriefingSections.RequestPart(brief.Text));
        Assert.DoesNotContain(Marker, BriefingSections.RequestPart(brief.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, BriefingSections.WithoutProfile(brief.Text), StringComparison.Ordinal);
        Assert.Equal(withProfile, BriefingSections.WithoutProfile(brief.Text).EndsWith(BriefingSections.ProfileHiddenNote, StringComparison.Ordinal));
        if (withProfile)
            Assert.StartsWith(BriefingSections.RequestPart(brief.Text), BriefingSections.WithoutProfile(brief.Text), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_profile_the_answers_stay_and_only_the_profile_is_replaced()
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var brief = composer.Compose(new OrderBriefInput(Order("user-a", Request), IntakeBriefComposerTests.Record(),
            [new("length", "short", null, false, AnswerOrigin.User)], Profile, AssumptionsShown: true)).Text;

        var hidden = BriefingSections.WithoutProfile(brief);

        Assert.Contains(IntakeBriefComposer.AnswersHeading, hidden, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.AssumptionsHeading, hidden, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.OpenFactsHeading, hidden, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.ProfileHeading, hidden, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("user-a", "user-a", true)]
    [InlineData("user-a", "admin", false)]
    [InlineData("user-a", null, false)]
    [InlineData(null, null, false)]
    public void Only_the_owner_sees_the_profile(string? owner, string? viewer, bool sees)
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var brief = composer.Compose(new OrderBriefInput(Order("user-a", Request), null, [], Profile)).Text;

        Assert.Equal(sees, BriefingSections.ForViewer(brief, owner, viewer).Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public void Briefings_that_were_not_composed_come_back_unchanged()
    {
        const string legacy = "Compose a crew for the following task:\n\nSchreib etwas.\n\n---\nNoch ein Absatz.";

        Assert.Equal(legacy, BriefingSections.RequestPart(legacy));
        Assert.Equal(legacy, BriefingSections.WithoutProfile(legacy));
        Assert.Equal(string.Empty, BriefingSections.RequestPart(string.Empty));
    }
}
