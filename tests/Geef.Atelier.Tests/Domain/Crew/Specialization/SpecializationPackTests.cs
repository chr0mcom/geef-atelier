using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Tests.Domain.Crew.Specialization;

public sealed class SpecializationPackTests
{
    [Theory]
    [InlineData("legal-terminology", true)]
    [InlineData("concise-output", true)]
    [InlineData("a", true)]
    [InlineData("Legal", false)]      // uppercase
    [InlineData("-legal", false)]     // leading hyphen
    [InlineData("legal-", false)]     // trailing hyphen
    [InlineData("legal_term", false)] // underscore
    [InlineData("", false)]
    public void IsValidName_ValidatesKebabCase(string name, bool expected)
    {
        Assert.Equal(expected, SpecializationPack.IsValidName(name));
    }

    [Fact]
    public void AppliesTo_EmptyList_MatchesEveryTarget()
    {
        IReadOnlyList<PackActorType> applicable = [];
        Assert.True(applicable.AppliesTo(PackActorType.Reviewer));
    }

    [Fact]
    public void AppliesTo_Any_MatchesEveryTarget()
    {
        IReadOnlyList<PackActorType> applicable = [PackActorType.Any];
        Assert.True(applicable.AppliesTo(PackActorType.Executor));
        Assert.True(applicable.AppliesTo(PackActorType.Grounding));
    }

    [Fact]
    public void AppliesTo_SpecificType_MatchesOnlyThatType()
    {
        IReadOnlyList<PackActorType> applicable = [PackActorType.Reviewer, PackActorType.Executor];
        Assert.True(applicable.AppliesTo(PackActorType.Reviewer));
        Assert.False(applicable.AppliesTo(PackActorType.Grounding));
    }
}
