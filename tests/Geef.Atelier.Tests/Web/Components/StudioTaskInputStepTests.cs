using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioTaskInputStepTests : TestContext
{
    [Fact]
    public void StudioTaskInputStep_RendersTextarea()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "");
        });

        cut.Find("[data-testid='studio-task-input-step']");
        cut.Find("[data-testid='task-description-input']");
    }

    [Fact]
    public void StudioTaskInputStep_AnalyzeButton_DisabledWhenEmpty()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "");
        });

        var button = cut.Find("[data-testid='analyze-button']");
        Assert.NotNull(button.GetAttribute("disabled"));
    }

    [Fact]
    public void StudioTaskInputStep_AnalyzeButton_EnabledWhenTextEntered()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "Write a detailed product report.");
        });

        var button = cut.Find("[data-testid='analyze-button']");
        Assert.Null(button.GetAttribute("disabled"));
    }
}
