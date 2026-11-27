using Geef.Atelier.Web.Display;

namespace Geef.Atelier.Tests.Web.Display;

public sealed class ReviewerDisplayTests
{
    [Theory]
    [InlineData("UnknownReviewer")]
    [InlineData("")]
    [InlineData("SomeOtherName")]
    public void UnknownReviewers_ReturnInputUnchanged(string input)
    {
        Assert.Equal(input, ReviewerDisplay.ToDisplay(input));
    }

    [Fact]
    public void CodeOnlyProfilesAndTemplatesShowTheirDisplayNames()
    {
        Assert.Equal("Automatic checks", ReviewerDisplay.ToDisplay("deterministic-checks"));
        Assert.Equal("Direct", ReviewerDisplay.GetTemplateDisplay("direct"));
        Assert.Equal("unknown-template", ReviewerDisplay.GetTemplateDisplay("unknown-template"));
    }
}
