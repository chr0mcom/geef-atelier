using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class FinalizersIndexTests : TestContext
{
    private static FinalizerProfile MakeFinalizer(
        string name,
        FinalizerType type = FinalizerType.FileExport,
        bool isSystem = false) => new(
        Name: name,
        DisplayName: name + " Display",
        Description: "desc",
        FinalizerType: type,
        Settings: [],
        IsSystem: isSystem,
        CreatedAt: isSystem ? null : DateTimeOffset.UtcNow,
        UpdatedAt: isSystem ? null : DateTimeOffset.UtcNow);

    [Fact]
    public void EmptyList_ShowsEmptyStateText()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizersIndex>();

        Assert.Contains("Keine Finalizer-Profile vorhanden.", cut.Markup);
    }

    [Fact]
    public void WithProfiles_TableIsRendered()
    {
        var profiles = new[] { MakeFinalizer("export-markdown", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizersIndex>();

        cut.Find("[data-testid='finalizer-list']");
    }

    [Fact]
    public void SystemProfile_RowHasViewLink()
    {
        var profiles = new[] { MakeFinalizer("export-markdown", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizersIndex>();
        var row = cut.Find("[data-testid='finalizer-row-export-markdown']");
        var viewLink = row.QuerySelector("a[href*='/view/export-markdown']");

        Assert.NotNull(viewLink);
    }

    [Fact]
    public void SystemProfile_DeleteButtonIsDisabled()
    {
        var profiles = new[] { MakeFinalizer("export-markdown", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizersIndex>();
        var row = cut.Find("[data-testid='finalizer-row-export-markdown']");
        var deleteBtn = row.QuerySelector("button[disabled]");

        Assert.NotNull(deleteBtn);
    }

    [Fact]
    public void CustomProfile_DeleteButtonClickShowsModal()
    {
        var profiles = new[] { MakeFinalizer("custom-my-finalizer", isSystem: false) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizersIndex>();
        var row = cut.Find("[data-testid='finalizer-row-custom-my-finalizer']");
        var deleteBtn = row.QuerySelector("button:not([disabled])");
        Assert.NotNull(deleteBtn);

        deleteBtn!.Click();

        cut.Find("[data-testid='delete-confirm-input']");
    }

    private sealed class StubCrewService(IEnumerable<FinalizerProfile>? profiles = null)
        : StubCrewServiceBase
    {
        private readonly List<FinalizerProfile> _profiles = profiles?.ToList() ?? [];

        public override Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(
            bool includeSystem = true, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FinalizerProfile>>(_profiles);
    }
}
