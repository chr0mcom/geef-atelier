using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderEditorModelAssignmentTests : Bunit.TestContext
{
    private static readonly IReadOnlyList<ModelInfo> Catalog = ModelMatrix.All
        .Select(c => c.Model)
        .Distinct()
        .Select(id => new ModelInfo(id, id, "", true))
        .ToList();

    private static GroundingProviderProfile Profile(Dictionary<string, string> settings) => new(
        Name: "custom-g",
        DisplayName: "G",
        Description: "d",
        ProviderType: "tavily",
        ProviderSettings: settings,
        MaxQueriesPerRun: 1,
        IsSystem: false);

    private static Dictionary<string, string> Refined(string provider, string model, bool managed)
    {
        var settings = new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementProvider] = provider,
            [GroundingProviderProfile.KeyRefinementModel] = model,
        };
        if (managed)
            settings[GroundingProviderProfile.KeyModelSlot] = nameof(ModelSlot.GroundingRefiner);
        return settings;
    }

    private CapturingCrewService Wire(GroundingProviderProfile? profile = null)
    {
        var crew = new CapturingCrewService(profile);
        Services.AddSingleton<ICrewService>(crew);
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "k" }));
        Services.AddSingleton<IModelCatalog>(new ResolvingModelCatalog(models: Catalog));
        this.AddTestAuthorization().SetAuthorized("user");
        return crew;
    }

    private static Task Save(IRenderedComponent<GroundingProviderEditor> cut) => cut.Find("form").SubmitAsync();

    [Fact]
    public async Task ManagedRefiner_KeepsTheMarker_AndShowsThePrimaryDisabled()
    {
        var crew = Wire(Profile(Refined("openrouter", "gpt-4o", managed: true)));

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        var primary = ModelMatrix.Primary(ModelSlot.GroundingRefiner);
        Assert.True(cut.Find("[data-testid='refinement-assignment-automatic']").HasAttribute("checked"));
        Assert.True(cut.Find("[data-testid='refinement-provider-select']").HasAttribute("disabled"));
        Assert.Equal(primary.Provider, cut.Find("[data-testid='refinement-provider-select']").GetAttribute("value"));

        await Save(cut);

        var saved = crew.Updated!.ProviderSettings;
        Assert.Equal(nameof(ModelSlot.GroundingRefiner), saved[GroundingProviderProfile.KeyModelSlot]);
        Assert.Equal(primary.Model, saved[GroundingProviderProfile.KeyRefinementModel]);
    }

    [Fact]
    public async Task SwitchingToManual_RemovesTheMarker()
    {
        var crew = Wire(Profile(Refined("claude-cli", "claude-sonnet-latest", managed: true)));
        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        cut.Find("[data-testid='refinement-assignment-manual']").Change("manual");
        Assert.False(cut.Find("[data-testid='refinement-provider-select']").HasAttribute("disabled"));
        await Save(cut);

        Assert.False(crew.Updated!.ProviderSettings.ContainsKey(GroundingProviderProfile.KeyModelSlot));
    }

    [Fact]
    public async Task ManualRefiner_OpensManual_AndHasNoMarkerAfterSave()
    {
        var crew = Wire(Profile(Refined("claude-cli", "claude-opus-latest", managed: false)));

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        Assert.True(cut.Find("[data-testid='refinement-assignment-manual']").HasAttribute("checked"));
        await Save(cut);

        Assert.False(crew.Updated!.ProviderSettings.ContainsKey(GroundingProviderProfile.KeyModelSlot));
        Assert.Equal("claude-opus-latest", crew.Updated.ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
    }

    [Fact]
    public async Task EnablingRefinement_DefaultsToAutomatic()
    {
        var crew = Wire();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-name']").Change("my-g");
        cut.Find("[data-testid='input-display-name']").Change("My G");

        cut.Find("[data-testid='refinement-enabled-checkbox']").Change(true);
        Assert.True(cut.Find("[data-testid='refinement-assignment-automatic']").HasAttribute("checked"));
        await Save(cut);

        var saved = crew.Created!.ProviderSettings;
        var primary = ModelMatrix.Primary(ModelSlot.GroundingRefiner);
        Assert.Equal(nameof(ModelSlot.GroundingRefiner), saved[GroundingProviderProfile.KeyModelSlot]);
        Assert.Equal(primary.Provider, saved[GroundingProviderProfile.KeyRefinementProvider]);
    }

    [Theory]
    [InlineData("codex-cli", "gpt-6-luna")]
    [InlineData("openrouter", "anthropic/claude-sonnet-4")]
    public void ManualRefiner_ShowsWhereARewrittenBindingWillBeSaved(string provider, string model)
    {
        Wire(Profile(Refined(provider, model, managed: false)));

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        Assert.Equal(
            ExpectedNotice(provider, model),
            cut.Find("[data-testid='refinement-cli-route-notice']").TextContent);
    }

    [Fact]
    public void ManualRefiner_ShowsNoNoticeForAKeptBinding()
    {
        Wire(Profile(Refined("claude-cli", "claude-sonnet-latest", managed: false)));

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        Assert.Empty(cut.FindAll("[data-testid='refinement-cli-route-notice']"));
    }

    [Fact]
    public void ManagedRefiner_ShowsNoNotice()
    {
        Wire(Profile(Refined("codex-cli", "gpt-6-luna", managed: true)));

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-g"));

        Assert.Empty(cut.FindAll("[data-testid='refinement-cli-route-notice']"));
    }

    private static string ExpectedNotice(string provider, string model)
    {
        var target = CliRoute.Normalize(provider, model);
        Assert.NotEqual(new ModelCandidate(provider, model), target);
        return $"This model will be saved as {target.Provider}/{target.Model} (CLI route / retired model).";
    }

    private sealed class CapturingCrewService(GroundingProviderProfile? profile) : StubCrewServiceBase
    {
        public GroundingProviderProfile? Created { get; private set; }
        public GroundingProviderProfile? Updated { get; private set; }

        public override Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(profile?.Name == name ? profile : null);

        public override Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
        {
            Created = p;
            return Task.FromResult(p);
        }

        public override Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
        {
            Updated = p;
            return Task.FromResult(p);
        }
    }
}
