using Bunit;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioConfirmationStepTests : TestContext
{
    [Fact]
    public void StudioConfirmationStep_RendersCreatedTemplateName()
    {
        var result = new MaterializationResult(
            CreatedTemplateName: "custom-legal-review",
            CreatedProfileNames: [],
            Warnings: []);

        var cut = RenderComponent<StudioConfirmationStep>(p =>
        {
            p.Add(c => c.Result, result);
        });

        cut.Find("[data-testid='studio-confirmation-step']");
        Assert.Contains("custom-legal-review", cut.Markup);
    }

    [Fact]
    public void StudioConfirmationStep_RendersCreatedProfileLinks()
    {
        var result = new MaterializationResult(
            CreatedTemplateName: "custom-legal-review",
            CreatedProfileNames: ["custom-legal-risk", "custom-jargon-checker"],
            Warnings: []);

        var cut = RenderComponent<StudioConfirmationStep>(p =>
        {
            p.Add(c => c.Result, result);
        });

        cut.Find("[data-testid='profile-link-custom-legal-risk']");
        cut.Find("[data-testid='profile-link-custom-jargon-checker']");
    }

    [Fact]
    public void StudioConfirmationStep_RendersWarnings_WhenPresent()
    {
        var result = new MaterializationResult(
            CreatedTemplateName: "custom-new-template",
            CreatedProfileNames: [],
            Warnings: ["Model 'x/y' is not currently available from provider 'unknown-provider'."]);

        var cut = RenderComponent<StudioConfirmationStep>(p =>
        {
            p.Add(c => c.Result, result);
        });

        cut.Find("[data-testid='warnings-banner']");
        Assert.Contains("unknown-provider", cut.Markup);
    }

    [Fact]
    public void StudioConfirmationStep_DoesNotRenderWarningsBanner_WhenNoWarnings()
    {
        var result = new MaterializationResult(
            CreatedTemplateName: "custom-template",
            CreatedProfileNames: [],
            Warnings: []);

        var cut = RenderComponent<StudioConfirmationStep>(p =>
        {
            p.Add(c => c.Result, result);
        });

        Assert.Throws<Bunit.ElementNotFoundException>(() =>
            cut.Find("[data-testid='warnings-banner']"));
    }
}
