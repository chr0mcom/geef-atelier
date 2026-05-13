using Bunit;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ReviewerPickerTests : TestContext
{
    private static ReviewerProfile MakeProfile(string name, string displayName, bool isSystem = false) => new(
        Name: name,
        DisplayName: displayName,
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        IsSystem: isSystem);

    private static IReadOnlyList<ReviewerProfile> DefaultProfiles() =>
        new[]
        {
            MakeProfile("alpha", "Alpha"),
            MakeProfile("beta", "Beta"),
            MakeProfile("gamma", "Gamma"),
        };

    [Fact]
    public void EmptyList_RendersEmptyState_NocrashOccurs()
    {
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, Array.Empty<ReviewerProfile>());
            p.Add(c => c.Selected, new List<string>());
        });

        cut.Find("[data-testid='reviewer-picker']");
        Assert.Contains("No reviewers selected", cut.Markup);
    }

    [Fact]
    public async Task AddReviewer_MovesFromAvailableToSelected()
    {
        List<string>? received = null;
        var selected = new List<string>();
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='reviewer-picker-available-alpha']").Click();

        Assert.NotNull(received);
        Assert.Contains("alpha", received!);
        // Should no longer appear in available
        cut.SetParametersAndRender(p =>
        {
            p.Add(c => c.Selected, received!);
        });
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='reviewer-picker-available-alpha']"));
    }

    [Fact]
    public async Task RemoveReviewer_MovesFromSelectedToAvailable()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        // The remove button (×) is inside the selected item for "alpha" — it has no data-testid but is the danger button
        var selectedItem = cut.Find("[data-testid='reviewer-picker-selected-alpha']");
        selectedItem.QuerySelector("button.danger")!.Click();

        Assert.NotNull(received);
        Assert.DoesNotContain("alpha", received!);
        Assert.Contains("beta", received!);
    }

    [Fact]
    public void MoveUp_OnFirstItem_UpButtonIsDisabled()
    {
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
        });

        var upBtn = cut.Find("[data-testid='reviewer-picker-up-alpha']");
        Assert.NotNull(upBtn.GetAttribute("disabled"));
    }

    [Fact]
    public void MoveDown_OnLastItem_DownButtonIsDisabled()
    {
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
        });

        var downBtn = cut.Find("[data-testid='reviewer-picker-down-beta']");
        Assert.NotNull(downBtn.GetAttribute("disabled"));
    }

    [Fact]
    public void MoveUp_OnSecondItem_SwapsWithFirst()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='reviewer-picker-up-beta']").Click();

        Assert.NotNull(received);
        Assert.Equal("beta", received![0]);
        Assert.Equal("alpha", received![1]);
    }

    [Fact]
    public void MoveDown_OnFirstItem_SwapsWithSecond()
    {
        List<string>? received = null;
        var selected = new List<string> { "alpha", "beta" };
        var cut = RenderComponent<ReviewerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, DefaultProfiles());
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, (List<string> s) => received = s);
        });

        cut.Find("[data-testid='reviewer-picker-down-alpha']").Click();

        Assert.NotNull(received);
        Assert.Equal("beta", received![0]);
        Assert.Equal("alpha", received![1]);
    }
}
