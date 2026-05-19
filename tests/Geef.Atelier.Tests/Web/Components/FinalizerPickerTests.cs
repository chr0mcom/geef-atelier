using Bunit;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class FinalizerPickerTests : TestContext
{
    private static FinalizerProfile MakeFinalizer(string name) => new(
        Name: name,
        DisplayName: name + " Display",
        Description: "desc",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "markdown" },
        IsSystem: true);

    [Fact]
    public void NoProfilesSelected_SelectedListIsEmpty()
    {
        var profiles = new[] { MakeFinalizer("export-markdown"), MakeFinalizer("export-html") };
        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, []);
        });

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("[data-testid='finalizer-picker-selected-export-markdown']"));
    }

    [Fact]
    public void AvailableProfiles_ListedInPicker()
    {
        var profiles = new[] { MakeFinalizer("export-markdown"), MakeFinalizer("export-html") };
        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, []);
        });

        cut.Find("[data-testid='finalizer-picker-available-export-markdown']");
        cut.Find("[data-testid='finalizer-picker-available-export-html']");
    }

    [Fact]
    public void AddProfile_ShowsInSelectedList()
    {
        var profiles = new[] { MakeFinalizer("export-markdown") };
        var selected = new List<string>();

        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, names => { selected = names; });
        });

        // The available-profile element IS the button element
        var addBtn = cut.Find("[data-testid='finalizer-picker-available-export-markdown']");
        addBtn.Click();

        Assert.Contains("export-markdown", selected);
    }

    [Fact]
    public void SelectedProfile_HasUpDownRemoveButtons()
    {
        var profiles = new[] { MakeFinalizer("export-markdown"), MakeFinalizer("export-html") };
        var selected = new List<string> { "export-markdown", "export-html" };

        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, selected);
        });

        cut.Find("[data-testid='finalizer-picker-up-export-markdown']");
        cut.Find("[data-testid='finalizer-picker-down-export-markdown']");
        cut.Find("[data-testid='finalizer-picker-remove-export-markdown']");
    }

    [Fact]
    public void RemoveButton_RemovesFromSelection()
    {
        var profiles = new[] { MakeFinalizer("export-markdown"), MakeFinalizer("export-html") };
        var selected = new List<string> { "export-markdown", "export-html" };

        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, names => { selected = names; });
        });

        var removeBtn = cut.Find("[data-testid='finalizer-picker-remove-export-markdown']");
        removeBtn.Click();

        Assert.DoesNotContain("export-markdown", selected);
        Assert.Contains("export-html", selected);
    }

    [Fact]
    public void UpButton_MovesProfileEarlierInOrder()
    {
        var profiles = new[] { MakeFinalizer("export-markdown"), MakeFinalizer("export-html") };
        var selected = new List<string> { "export-markdown", "export-html" };

        var cut = RenderComponent<FinalizerPicker>(p =>
        {
            p.Add(c => c.AllProfiles, profiles);
            p.Add(c => c.Selected, selected);
            p.Add(c => c.SelectedChanged, names => { selected = names; });
        });

        // Move export-html (index 1) up
        var upBtn = cut.Find("[data-testid='finalizer-picker-up-export-html']");
        upBtn.Click();

        Assert.Equal("export-html", selected[0]);
        Assert.Equal("export-markdown", selected[1]);
    }
}
