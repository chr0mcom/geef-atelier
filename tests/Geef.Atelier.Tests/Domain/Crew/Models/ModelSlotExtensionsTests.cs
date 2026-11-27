using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class ModelSlotExtensionsTests
{
    [Theory]
    [InlineData(ModelSlot.FactReviewer, true)]
    [InlineData(ModelSlot.StyleReviewer, true)]
    [InlineData(ModelSlot.DomainReviewer, true)]
    [InlineData(ModelSlot.Helper, true)]
    [InlineData(ModelSlot.Executor, false)]
    [InlineData(ModelSlot.Intake, false)]
    [InlineData(ModelSlot.GroundingRefiner, false)]
    public void OnlyTheReviewSlotsHaveTheReviewerRole(ModelSlot slot, bool reviewer) =>
        Assert.Equal(reviewer, slot.IsReviewerRole());
}
