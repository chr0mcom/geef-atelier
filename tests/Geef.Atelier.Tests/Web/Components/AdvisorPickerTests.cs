using Bunit;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class AdvisorPickerTests : TestContext
{
    private static AdvisorProfile MakeProfile(string name, string displayName, bool isSystem = false) => new(
        Name: name,
        DisplayName: displayName,
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        Mode: AdvisorMode.Strategic,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: isSystem);

    private static IReadOnlyList<AdvisorProfile> DefaultProfiles() =>
        new[]
        {
            MakeProfile("alpha", "Alpha"),
            MakeProfile("beta", "Beta"),
            MakeProfile("gamma", "Gamma"),
        };

    [Fact]
    public void AdvisorPicker_RendersAvailableProfiles()
    {
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, new List<string>());
        });

        cut.Find("[data-testid='advisor-picker']");
        cut.Find("[data-testid='advisor-picker-available-alpha']");
        cut.Find("[data-testid='advisor-picker-available-beta']");
        cut.Find("[data-testid='advisor-picker-available-gamma']");
    }

    [Fact]
    public async Task AdvisorPicker_AddProfile_MovesToSelected()
    {
        List<string>? received = null;
        var selected = new List<string>();
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='advisor-picker-available-alpha']").Click();

        Assert.NotNull(received);
        Assert.Contains("alpha", received!);
        // Should no longer appear in available after re-render
        cut.SetParametersAndRender(p =>
        {
            p.Add(c => c.Selected, received!);
        });
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='advisor-picker-available-alpha']"));
    }

    [Fact]
    public async Task AdvisorPicker_RemoveProfile_MovesToAvailable()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        var selectedItem = cut.Find("[data-testid='advisor-picker-selected-alpha']");
        selectedItem.QuerySelector("button.danger")!.Click();

        Assert.NotNull(received);
        Assert.DoesNotContain("alpha", received!);
        Assert.Contains("beta", received!);
    }

    [Fact]
    public void AdvisorPicker_MoveUp_ChangesOrder()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='advisor-picker-up-beta']").Click();

        Assert.NotNull(received);
        Assert.Equal("beta", received![0]);
        Assert.Equal("alpha", received![1]);
    }

    [Fact]
    public void AdvisorPicker_MoveDown_ChangesOrder()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='advisor-picker-down-alpha']").Click();

        Assert.NotNull(received);
        Assert.Equal("beta", received![0]);
        Assert.Equal("alpha", received![1]);
    }

    [Fact]
    public void AdvisorPicker_MoveUp_DoesNothing_WhenFirstItem()
    {
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
        });

        var upBtn = cut.Find("[data-testid='advisor-picker-up-alpha']");
        Assert.NotNull(upBtn.GetAttribute("disabled"));
    }

    [Fact]
    public void AdvisorPicker_MoveDown_DoesNothing_WhenLastItem()
    {
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<AdvisorPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
        });

        var downBtn = cut.Find("[data-testid='advisor-picker-down-beta']");
        Assert.NotNull(downBtn.GetAttribute("disabled"));
    }
}
