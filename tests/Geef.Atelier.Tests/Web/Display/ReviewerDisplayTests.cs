using Geef.Atelier.Web.Display;

namespace Geef.Atelier.Tests.Web.Display;

public sealed class ReviewerDisplayTests
{
    [Theory]
    [InlineData("BriefingTreueReviewer", "BriefingFidelity")]
    [InlineData("KlarheitReviewer",      "Clarity")]
    public void KnownReviewers_ReturnMappedDisplayName(string input, string expected)
    {
        Assert.Equal(expected, ReviewerDisplay.ToDisplay(input));
    }

    [Theory]
    [InlineData("UnknownReviewer")]
    [InlineData("")]
    [InlineData("SomeOtherName")]
    public void UnknownReviewers_ReturnInputUnchanged(string input)
    {
        Assert.Equal(input, ReviewerDisplay.ToDisplay(input));
    }
}
