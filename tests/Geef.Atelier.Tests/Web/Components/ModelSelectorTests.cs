using Bunit;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// The picker is where the whole change becomes visible, so it must say the truth about where its
/// list came from: a red "unreachable" banner only for a failed lookup, a quiet note for a provider
/// that never had a live source, and nothing at all before the first lookup has happened.
/// </summary>
public sealed class ModelSelectorTests : Bunit.TestContext
{
    private static readonly IReadOnlyList<ModelInfo> Models =
    [
        new("claude-opus-latest", "Claude Opus (latest)", "Best quality", true),
        new("claude-sonnet-latest", "Claude Sonnet (latest)", "Balanced", true),
        new("claude-opus-5", "Claude Opus 5", "Pinned", false),
    ];

    private IRenderedComponent<ModelSelector> Render(
        ModelCatalogSource source, IReadOnlyList<ModelInfo>? models = null, string value = "")
    {
        Services.AddSingleton<IModelCatalog>(new StubCatalog(source, models ?? Models));
        return RenderComponent<ModelSelector>(p => p
            .Add(c => c.Provider, "claude-cli")
            .Add(c => c.Value, value));
    }

    [Fact]
    public void RendersTheCatalogGroupedIntoRecommendedAndOther()
    {
        var cut = Render(ModelCatalogSource.Live);

        cut.Find("[data-testid=model-selector-trigger]").Click();

        var groups = cut.FindAll(".model-group-label").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["Recommended", "Other available"], groups);

        var items = cut.FindAll(".model-list-item").Select(e => e.TextContent.Trim()).ToList();
        Assert.Contains("Claude Opus (latest)", items);
        Assert.Contains("Claude Opus 5", items);
    }

    [Fact]
    public void AutoSelectsTheFirstRecommendedModel()
    {
        var cut = Render(ModelCatalogSource.Live);

        Assert.Contains("Claude Opus (latest)", cut.Find(".trigger-label").TextContent);
    }

    [Fact]
    public void ShowsTheUnreachableBannerOnlyForAFailedLookup()
    {
        var cut = Render(ModelCatalogSource.Fallback);

        Assert.Contains("provider unreachable", cut.Find(".model-selector-fallback-banner").TextContent);
        Assert.Empty(cut.FindAll(".model-selector-static-note"));
    }

    [Fact]
    public void ShowsAQuietNoteAndNoRefreshForAProviderWithoutALiveSource()
    {
        var cut = Render(ModelCatalogSource.StaticByDesign);

        Assert.Contains("no live model endpoint", cut.Find(".model-selector-static-note").TextContent);
        Assert.Empty(cut.FindAll(".model-selector-fallback-banner"));
        Assert.Empty(cut.FindAll(".model-selector-refresh"));
    }

    [Fact]
    public void ShowsNoBannerAtAllBeforeTheFirstLookupHasHappened()
    {
        var cut = Render(ModelCatalogSource.Unknown);

        Assert.Empty(cut.FindAll(".model-selector-fallback-banner"));
        Assert.Empty(cut.FindAll(".model-selector-static-note"));
    }

    [Theory]
    [InlineData(ModelCatalogSource.Live)]
    [InlineData(ModelCatalogSource.Fallback)]
    public void KeepsTheRefreshButtonWhereRefreshingCanChangeSomething(ModelCatalogSource source)
    {
        Assert.Single(Render(source).FindAll(".model-selector-refresh"));
    }

    [Fact]
    public void FallsBackToTheFreeTextFieldForAValueTheCatalogDoesNotKnow()
    {
        var cut = Render(ModelCatalogSource.Live, value: "some-retired-model");

        Assert.Equal("some-retired-model", cut.Find("[data-testid=model-selector-custom-input]").GetAttribute("value"));
    }

    private sealed class StubCatalog(ModelCatalogSource source, IReadOnlyList<ModelInfo> models) : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult(models);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult(models);
        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult(models);
        public ModelCatalogSource GetSource(string providerName) => source;
        public bool IsUsingFallback(string providerName) => source == ModelCatalogSource.Fallback;
        public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
            => Task.FromResult(modelId);
    }
}
