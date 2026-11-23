using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class FinalizerEditorModelAssignmentTests : Bunit.TestContext
{
    private static readonly IReadOnlyList<ModelInfo> Catalog = ModelMatrix.All
        .Select(c => c.Model)
        .Distinct()
        .Select(id => new ModelInfo(id, id, "", true))
        .ToList();

    private static FinalizerProfile TransformProfile(Dictionary<string, string> settings) => new(
        Name: "custom-transform",
        DisplayName: "Transform",
        Description: "d",
        FinalizerType: FinalizerType.Transform,
        Settings: settings,
        IsSystem: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private CapturingCrewService Wire(FinalizerProfile? profile = null)
    {
        var crew = new CapturingCrewService(profile);
        Services.AddSingleton<ICrewService>(crew);
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        Services.AddSingleton<IModelCatalog>(new ResolvingModelCatalog(models: Catalog));
        this.AddTestAuthorization().SetAuthorized("user");
        return crew;
    }

    private static bool IsChecked(IRenderedComponent<FinalizerEditor> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").HasAttribute("checked");

    private static async Task Save(IRenderedComponent<FinalizerEditor> cut) =>
        await cut.Find("form").SubmitAsync();

    private IRenderedComponent<FinalizerEditor> RenderNewTransform()
    {
        var cut = RenderComponent<FinalizerEditor>();
        cut.Find("[data-testid='input-name']").Change("my-transform");
        cut.Find("[data-testid='input-display-name']").Change("My transform");
        cut.Find("[data-testid='input-finalizer-type']").Change(nameof(FinalizerType.Transform));
        cut.Find("[data-testid='input-system-prompt']").Change("Rewrite it.");
        return cut;
    }

    [Fact]
    public void NewTransform_DefaultsToAutomaticWithTheCondensePrimary()
    {
        Wire();

        var cut = RenderNewTransform();

        var primary = ModelMatrix.Primary(ModelSlot.TransformCondense);
        Assert.True(IsChecked(cut, "transform-assignment-automatic"));
        Assert.Equal(nameof(ModelSlot.TransformCondense), cut.Find("[data-testid='transform-slot-select']").GetAttribute("value"));
        Assert.Equal(primary.Provider, cut.Find("[data-testid='input-transform-provider']").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid='input-transform-provider']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='model-selector-trigger']").HasAttribute("disabled"));
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);
    }

    [Fact]
    public void RoleSelect_OffersOnlyTheTransformSlots()
    {
        Wire();

        var cut = RenderNewTransform();

        var offered = cut.FindAll("[data-testid='transform-slot-select'] option").Select(o => o.GetAttribute("value")!).ToArray();
        Assert.Equal([nameof(ModelSlot.TransformRewrite), nameof(ModelSlot.TransformCondense)], offered);
    }

    [Fact]
    public async Task SaveInAutomatic_WritesTheMarkerAndThePrimaryBinding()
    {
        var crew = Wire();
        var cut = RenderNewTransform();
        cut.Find("[data-testid='transform-slot-select']").Change(nameof(ModelSlot.TransformRewrite));

        await Save(cut);

        Assert.True(crew.Created is not null, cut.Markup);
        var settings = crew.Created!.Settings;
        var primary = ModelMatrix.Primary(ModelSlot.TransformRewrite);
        Assert.Equal(nameof(ModelSlot.TransformRewrite), settings[TransformSettings.KeyModelSlot]);
        Assert.Equal(primary.Provider, settings[TransformSettings.KeyProvider]);
        Assert.Equal(primary.Model, settings[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task SaveInManual_RemovesTheMarker()
    {
        var primary = ModelMatrix.Primary(ModelSlot.TransformCondense);
        var crew = Wire(TransformProfile(new TransformSettings("p", primary.Provider, primary.Model, 4096, null, ModelSlot.TransformCondense).ToDict()));
        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));
        Assert.True(IsChecked(cut, "transform-assignment-automatic"));

        cut.Find("[data-testid='transform-assignment-manual']").Change("manual");
        await Save(cut);

        Assert.False(crew.Updated!.Settings.ContainsKey(TransformSettings.KeyModelSlot));
        Assert.Equal(primary.Model, crew.Updated.Settings[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task ExistingTransformWithoutMarker_OpensInManual_AndStaysManual()
    {
        var crew = Wire(TransformProfile(new TransformSettings("p", "claude-cli", "claude-opus-latest", 4096).ToDict()));

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        Assert.True(IsChecked(cut, "transform-assignment-manual"));
        Assert.False(cut.Find("[data-testid='input-transform-provider']").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[data-testid='transform-slot-select']"));

        await Save(cut);

        Assert.False(crew.Updated!.Settings.ContainsKey(TransformSettings.KeyModelSlot));
        Assert.Equal("claude-opus-latest", crew.Updated.Settings[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task SwitchingManualToAutomatic_WritesTheMarkerAgain()
    {
        var crew = Wire(TransformProfile(new TransformSettings("p", "claude-cli", "claude-opus-latest", 4096).ToDict()));
        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        cut.Find("[data-testid='transform-assignment-automatic']").Change("automatic");
        await Save(cut);

        Assert.Equal(nameof(ModelSlot.TransformCondense), crew.Updated!.Settings[TransformSettings.KeyModelSlot]);
    }

    [Fact]
    public async Task UnknownSettingsKeys_ArePreservedOnSave()
    {
        var settings = new TransformSettings("p", "claude-cli", "claude-opus-latest", 4096).ToDict();
        settings["FutureKey"] = "keep-me";
        var crew = Wire(TransformProfile(settings));
        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        await Save(cut);

        Assert.Equal("keep-me", crew.Updated!.Settings["FutureKey"]);
    }

    [Fact]
    public async Task TheSavedProfile_ReadsBackWithTheSameSlot()
    {
        var crew = Wire();
        var cut = RenderNewTransform();

        await Save(cut);

        var read = TransformSettings.From(crew.Created!.Settings);
        Assert.Equal(ModelSlot.TransformCondense, read.ModelSlot);
        Assert.Equal(ModelMatrix.Primary(ModelSlot.TransformCondense).Model, read.Model);
    }

    private static Dictionary<string, string> WithoutBinding() => new()
    {
        [TransformSettings.KeySystemPrompt] = "Condense it.",
        [TransformSettings.KeyMaxTokens] = "4096",
    };

    [Fact]
    public async Task ExistingTransformWithoutBindingAndMarker_OpensInAutomaticWithTheCondenseRole()
    {
        var crew = Wire(TransformProfile(WithoutBinding()));

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        var primary = ModelMatrix.Primary(ModelSlot.TransformCondense);
        Assert.True(IsChecked(cut, "transform-assignment-automatic"));
        Assert.False(IsChecked(cut, "transform-assignment-manual"));
        Assert.Equal(nameof(ModelSlot.TransformCondense), cut.Find("[data-testid='transform-slot-select']").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid='input-transform-provider']").HasAttribute("disabled"));
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);

        await Save(cut);

        var saved = crew.Updated!.Settings;
        Assert.Equal(nameof(ModelSlot.TransformCondense), saved[TransformSettings.KeyModelSlot]);
        Assert.Equal(primary.Provider, saved[TransformSettings.KeyProvider]);
        Assert.Equal(primary.Model, saved[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task ExistingTransformWithBindingButWithoutMarker_StaysManualOnSave()
    {
        var crew = Wire(TransformProfile(new TransformSettings("p", "claude-cli", "claude-sonnet-latest", 4096).ToDict()));

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));
        await Save(cut);

        Assert.True(IsChecked(cut, "transform-assignment-manual"));
        Assert.False(crew.Updated!.Settings.ContainsKey(TransformSettings.KeyModelSlot));
    }

    [Theory]
    [InlineData("codex-cli", "gpt-6-luna")]
    [InlineData("codex-cli", "gpt-5.6-sol")]
    [InlineData("openrouter", "anthropic/claude-sonnet-4")]
    public void Manual_ShowsWhereARewrittenBindingWillBeSaved(string provider, string model)
    {
        Wire(TransformProfile(new TransformSettings("p", provider, model, 4096).ToDict()));

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        Assert.Equal(
            ExpectedNotice(provider, model),
            cut.Find("[data-testid='transform-cli-route-notice']").TextContent);
    }

    [Fact]
    public void Manual_ShowsNoNoticeForABindingThatIsKept_AndNoneInAutomatic()
    {
        Wire(TransformProfile(new TransformSettings("p", "codex-cli", "gpt-6-luna", 4096).ToDict()));
        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));
        Assert.Single(cut.FindAll("[data-testid='transform-cli-route-notice']"));

        cut.Find("[data-testid='transform-assignment-automatic']").Change("automatic");

        Assert.Empty(cut.FindAll("[data-testid='transform-cli-route-notice']"));
    }

    [Fact]
    public void Manual_ShowsNoNoticeForAKeptBinding()
    {
        Wire(TransformProfile(new TransformSettings("p", "claude-cli", "claude-opus-latest", 4096).ToDict()));

        var cut = RenderComponent<FinalizerEditor>(p => p.Add(c => c.Name, "custom-transform"));

        Assert.Empty(cut.FindAll("[data-testid='transform-cli-route-notice']"));
    }

    private static string ExpectedNotice(string provider, string model)
    {
        var target = CliRoute.Normalize(provider, model);
        Assert.NotEqual(new ModelCandidate(provider, model), target);
        return $"This model will be saved as {target.Provider}/{target.Model} (CLI route / retired model).";
    }

    private sealed class CapturingCrewService(FinalizerProfile? profile) : StubCrewServiceBase
    {
        public FinalizerProfile? Created { get; private set; }
        public FinalizerProfile? Updated { get; private set; }

        public override Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(profile?.Name == name ? profile : null);

        public override Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default)
        {
            Created = p;
            return Task.FromResult(p);
        }

        public override Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default)
        {
            Updated = p;
            return Task.FromResult(p);
        }
    }
}
