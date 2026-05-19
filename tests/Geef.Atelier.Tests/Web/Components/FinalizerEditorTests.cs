using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class FinalizerEditorTests : TestContext
{
    private static FinalizerProfile MakeFinalizer(
        string name, FinalizerType type = FinalizerType.FileExport) => new(
        Name: name,
        DisplayName: name + " Display",
        Description: "test description",
        FinalizerType: type,
        Settings: type == FinalizerType.FileExport
            ? new() { [FileExportSettings.KeyFormat] = "markdown" }
            : [],
        IsSystem: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Create_RendersEditorForm()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizerEditor>();

        cut.Find("[data-testid='finalizer-editor']");
        cut.Find("[data-testid='input-name']");
        cut.Find("[data-testid='input-finalizer-type']");
    }

    [Fact]
    public void Create_DefaultType_ShowsFileExportFields()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizerEditor>();

        // Default type is FileExport — export-format select should be visible
        cut.Find("[data-testid='input-export-format']");
    }

    [Fact]
    public void Edit_ExistingMetadataEnrich_ShowsEnricherTypeField()
    {
        var profile = MakeFinalizer("custom-enrich", FinalizerType.MetadataEnrich);
        var crew = new StubCrewService(profile);
        Services.AddSingleton<ICrewService>(crew);
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(e => e.Name, "custom-enrich"));

        cut.Find("[data-testid='input-enricher-type']");
    }

    [Fact]
    public void Edit_ExistingExternalSink_ShowsSinkFields()
    {
        var settings = new WebhookSinkSettings("https://example.com/hook", null, "application/json", 30).ToDict();
        var profile = new FinalizerProfile(
            Name: "custom-webhook",
            DisplayName: "Webhook",
            Description: "test",
            FinalizerType: FinalizerType.ExternalSink,
            Settings: settings,
            IsSystem: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
        var crew = new StubCrewService(profile);
        Services.AddSingleton<ICrewService>(crew);
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(e => e.Name, "custom-webhook"));

        cut.Find("[data-testid='input-sink-kind']");
    }

    [Fact]
    public void Edit_TypeDropdown_IsDisabled()
    {
        var profile = MakeFinalizer("custom-export", FinalizerType.FileExport);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("user");

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(e => e.Name, "custom-export"));

        var typeSelect = cut.Find("[data-testid='input-finalizer-type']");
        Assert.NotNull(typeSelect.GetAttribute("disabled"));
    }

    private sealed class StubCrewService(FinalizerProfile? profile = null) : StubCrewServiceBase
    {
        public override Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(profile?.Name == name ? profile : null);
    }
}
