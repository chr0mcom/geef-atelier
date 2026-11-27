using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The structural rule behind “This version is longer than the one before”: more than 115 % of the words of the text that was changed,
/// counted like the stored word count, only for versions that changed an earlier one on the person's request. No list of words decides it.
/// </summary>
public sealed class RevisionLengthTests
{
    [Theory]
    [InlineData(100, 115, false)]
    [InlineData(100, 116, true)]
    [InlineData(20, 23, false)]
    [InlineData(20, 24, true)]
    [InlineData(241, 351, true)]
    [InlineData(214, 240, false)]
    [InlineData(215, 215, false)]
    [InlineData(182, 141, false)]
    [InlineData(0, 50, false)]
    [InlineData(1, 0, false)]
    public void A_version_is_clearly_longer_only_above_115_percent(int baseWords, int newWords, bool expected) =>
        Assert.Equal(expected, RevisionLength.IsClearlyLonger(baseWords, newWords));

    [Fact]
    public void Large_counts_do_not_overflow() =>
        Assert.True(RevisionLength.IsClearlyLonger(int.MaxValue / 2, int.MaxValue));

    [Fact]
    public void A_changed_version_compares_its_text_with_the_text_it_was_given()
    {
        var seed = Words(20);

        Assert.True(Run(seed, Words(24), "Bitte mach den Text länger.").GrewBeyondItsBase());
        Assert.False(Run(seed, Words(23), "Bitte mach den Text länger.").GrewBeyondItsBase());
        Assert.False(Run(seed, Words(10), "Make the text about a third shorter.").GrewBeyondItsBase());
    }

    [Fact]
    public void Words_are_counted_like_the_stored_word_count()
    {
        var seed = "Liebe Eltern,\n\nder Elternabend ist am 12. Mai – um 19 Uhr.";
        var longer = seed + " Bitte bringt eure Fragen mit.";

        Assert.Equal(12, WordCounter.Count(seed));
        Assert.Equal(17, WordCounter.Count(longer));
        Assert.True(Run(seed, longer, "Add that questions are welcome.").GrewBeyondItsBase());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void A_version_without_a_change_request_never_gets_the_sentence(string? hint) =>
        Assert.False(Run(Words(10), Words(30), hint).GrewBeyondItsBase());

    [Fact]
    public void A_first_version_or_a_version_without_text_never_gets_the_sentence()
    {
        Assert.False(Run(null, Words(30), "Longer please.").GrewBeyondItsBase());
        Assert.False(Run(Words(10), null, "Longer please.").GrewBeyondItsBase());
        Assert.False(Run(Words(10), " \n ", "Longer please.").GrewBeyondItsBase());
    }

    [Fact]
    public void Only_the_former_button_longer_is_not_offered()
    {
        Assert.Equal([RevisionQuick.Shorter, RevisionQuick.MoreFormal, RevisionQuick.Friendlier, RevisionQuick.Simpler], RevisionQuickText.Offered);
        Assert.False(RevisionQuick.Longer.IsOffered());
        Assert.All(Enum.GetValues<RevisionQuick>().Where(q => q != RevisionQuick.Longer), q => Assert.True(q.IsOffered()));
        Assert.Equal("Longer", RevisionQuick.Longer.Label());
        Assert.Equal("Make the text about a third longer without adding new facts.", RevisionQuick.Longer.Instruction());
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Range(1, count).Select(i => $"word{i}"));

    private static RunEntity Run(string? seed, string? text, string? hint) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UnixEpoch,
        Status = RunStatus.Completed,
        BriefingText = "brief",
        ConfigJson = "{}",
        SeedDraftText = seed,
        FinalText = text,
        RevisionHint = hint,
    };
}
