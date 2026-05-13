using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// bUnit tests for the <c>Press</c> component, focusing on the grounding-stage visualization.
/// </summary>
public sealed class PressVisualizationWithGroundingTests : TestContext
{
    [Fact]
    public void PressGrounding_TestId_AlwaysPresent()
    {
        var cut = RenderComponent<Press>(p => p.Add(c => c.Status, RunStatus.Pending));

        cut.Find("[data-testid='press-grounding']");
    }

    [Fact]
    public void PressDivider_AlwaysPresent()
    {
        var cut = RenderComponent<Press>(p => p.Add(c => c.Status, RunStatus.Pending));

        cut.Find(".press-divider");
    }

    [Fact]
    public void GroundingStage_Pending_HasActiveClass_NotDone()
    {
        var cut = RenderComponent<Press>(p => p.Add(c => c.Status, RunStatus.Pending));

        var groundingDiv = cut.Find("[data-testid='press-grounding']");
        var stage        = groundingDiv.QuerySelector(".stage");

        Assert.NotNull(stage);
        Assert.Contains("active", stage.ClassList);
        Assert.DoesNotContain("done", stage.ClassList);
    }

    [Fact]
    public void GroundingStage_Running_HasDoneClass()
    {
        var cut = RenderComponent<Press>(p => p.Add(c => c.Status, RunStatus.Running));

        var groundingDiv = cut.Find("[data-testid='press-grounding']");
        var stage        = groundingDiv.QuerySelector(".stage");

        Assert.NotNull(stage);
        Assert.Contains("done", stage.ClassList);
    }

    [Fact]
    public void GroundingStage_Completed_HasDoneClass()
    {
        var cut = RenderComponent<Press>(p => p.Add(c => c.Status, RunStatus.Completed));

        var groundingDiv = cut.Find("[data-testid='press-grounding']");
        var stage        = groundingDiv.QuerySelector(".stage");

        Assert.NotNull(stage);
        Assert.Contains("done", stage.ClassList);
    }

    [Fact]
    public void PressGrounding_ExistsForAllStatuses()
    {
        foreach (var status in Enum.GetValues<RunStatus>())
        {
            var cut = RenderComponent<Press>(p => p.Add(c => c.Status, status));
            cut.Find(".press-grounding"); // throws ElementNotFoundException on failure
        }
    }
}
