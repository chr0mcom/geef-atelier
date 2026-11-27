using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class ModelParserTests
{
    [Theory]
    [InlineData("FactReviewer", ModelSlot.FactReviewer)]
    [InlineData("fact_reviewer", ModelSlot.FactReviewer)]
    [InlineData("Fact-Reviewer", ModelSlot.FactReviewer)]
    [InlineData("transform rewrite", ModelSlot.TransformRewrite)]
    public void SlotParserIsTolerant(string raw, ModelSlot expected)
    {
        Assert.True(ModelSlotParser.TryParse(raw, out var slot));
        Assert.Equal(expected, slot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("Reviewer")]
    [InlineData("FactReviwer")]
    [InlineData("FactReviewer,StyleReviewer")]
    [InlineData("FactReviewer|StyleReviewer")]
    public void SlotParserRejectsUnknownAndNumericValues(string? raw)
    {
        Assert.False(ModelSlotParser.TryParse(raw, out _));
        Assert.Null(ModelSlotParser.ParseOrNull(raw));
    }

    [Fact]
    public void EnumValuesAreStableBecauseSnapshotsStoreNumbers()
    {
        Assert.Equal(0, (int)ModelSlot.Executor);
        Assert.Equal(9, (int)ModelSlot.TransformCondense);
        Assert.Equal(11, (int)ModelSlot.Helper);
        Assert.Equal(12, (int)ModelSlot.Intake);
        Assert.Equal(0, (int)ModelDomain.General);
        Assert.Equal(6, (int)ModelDomain.Technical);
    }

    [Theory]
    [InlineData("Legal", ModelDomain.Legal)]
    [InlineData(" legal ", ModelDomain.Legal)]
    [InlineData("+2", null)]
    [InlineData("-1", null)]
    [InlineData("2", null)]
    [InlineData("General,Legal", null)]
    [InlineData("Legal|General", null)]
    [InlineData("", null)]
    public void ExactDomainParsingAcceptsOnlyASingleDeclaredName(string raw, ModelDomain? expected)
    {
        Assert.Equal(expected, ModelDomainParser.TryParseExact(raw));
    }

    [Theory]
    [InlineData("+1")]
    [InlineData("FactReviewer, StyleReviewer")]
    public void SlotParsingRejectsSignedAndCombinedValues(string raw)
    {
        Assert.False(ModelSlotParser.TryParse(raw, out _));
    }
}
