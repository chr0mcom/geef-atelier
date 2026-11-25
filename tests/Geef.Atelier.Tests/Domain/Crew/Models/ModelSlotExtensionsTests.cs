using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class ModelSlotExtensionsTests
{
    [Fact]
    public void IntakeIsNotAllowedForAnyProfileKind()
    {
        foreach (var kind in Enum.GetValues<ModelActorKind>())
        {
            Assert.DoesNotContain(ModelSlot.Intake, kind.AllowedSlots());
            Assert.False(ModelSlot.Intake.IsAllowedFor(kind));
        }
    }

    [Fact]
    public void IntakeHasItsOwnDescriptionAndCandidates()
    {
        Assert.Equal("Prepares an order before writing: what is missing and what to ask.", ModelSlot.Intake.Describe());
        Assert.False(ModelSlot.Intake.IsReviewerRole());
        Assert.False(ModelSlot.Intake.IsAdvisorRole());
    }
}
