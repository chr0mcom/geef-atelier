using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class GapTopicKindsTests
{
    public static TheoryData<GapTopic, bool> AllTopics => new()
    {
        { GapTopic.Length, false },
        { GapTopic.Audience, false },
        { GapTopic.Purpose, false },
        { GapTopic.Tone, false },
        { GapTopic.Formality, false },
        { GapTopic.Language, false },
        { GapTopic.Sender, true },
        { GapTopic.Signature, true },
        { GapTopic.Facts, true },
        { GapTopic.Format, false },
        { GapTopic.Date, true },
        { GapTopic.Scope, false },
        { GapTopic.Other, true },
    };

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_topic_is_sorted_into_form_or_content(GapTopic topic, bool content) =>
        Assert.Equal(content, topic.IsContent());

    [Fact]
    public void A_new_topic_makes_this_test_red_until_it_is_sorted()
    {
        var listed = AllTopics.Select(row => (GapTopic)row[0]).ToHashSet();

        Assert.Equal(13, Enum.GetValues<GapTopic>().Length);
        Assert.True(Enum.GetValues<GapTopic>().All(listed.Contains));
    }

    [Fact]
    public void An_unknown_topic_counts_as_content() =>
        Assert.True(((GapTopic)99).IsContent());
}
