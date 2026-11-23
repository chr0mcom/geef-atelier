using Bunit;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.UI;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ProfileEditorFormModelAssignmentTests : Bunit.TestContext
{
    private static readonly IReadOnlyList<ModelInfo> Catalog = ModelMatrix.All
        .Select(c => c.Model)
        .Distinct()
        .Select(id => new ModelInfo(id, id, "", true))
        .ToList();

    public ProfileEditorFormModelAssignmentTests()
    {
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        Services.AddSingleton<IToolDefinitionService>(new NoTools());
        Services.AddSingleton<IModelCatalog>(new ResolvingModelCatalog(models: Catalog));
    }

    private IRenderedComponent<ProfileEditorForm> Render(
        ModelActorKind kind,
        ModelSlot? slot = null,
        ModelDomain? domain = null,
        bool isNew = false,
        bool isSystem = false,
        string provider = "claude-cli",
        string model = "claude-opus-latest",
        AdvisorMode? mode = null,
        bool showTools = false,
        Action<ProfileEditorForm.ProfileFormValues>? onSave = null) =>
        RenderComponent<ProfileEditorForm>(p =>
        {
            p.Add(c => c.ActorKind, kind)
             .Add(c => c.ModelSlot, slot)
             .Add(c => c.ModelDomain, domain)
             .Add(c => c.IsSystem, isSystem)
             .Add(c => c.ShowToolPicker, showTools)
             .Add(c => c.Provider, provider)
             .Add(c => c.Model, model)
             .Add(c => c.DisplayName, "Display")
             .Add(c => c.SystemPrompt, "Prompt")
             .Add(c => c.ProfileName, isNew ? null : "custom-profile")
             .Add(c => c.OnSave, EventCallback.Factory.Create<ProfileEditorForm.ProfileFormValues>(this, v => onSave?.Invoke(v)));
            if (kind == ModelActorKind.Advisor)
                p.Add(c => c.ShowAdvisorFields, true)
                 .Add(c => c.SelectedMode, mode)
                 .Add(c => c.SelectedModeChanged, EventCallback.Factory.Create<AdvisorMode?>(this, _ => { }));
        });

    private static string Value(IRenderedComponent<ProfileEditorForm> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").GetAttribute("value")!;

    private static bool Disabled(IRenderedComponent<ProfileEditorForm> cut, string selector) =>
        cut.Find(selector).HasAttribute("disabled");

    private static void SelectManual(IRenderedComponent<ProfileEditorForm> cut) =>
        cut.Find("[data-testid='model-assignment-manual']").Change("manual");

    private static void SelectAutomatic(IRenderedComponent<ProfileEditorForm> cut) =>
        cut.Find("[data-testid='model-assignment-automatic']").Change("automatic");

    private static void Submit(IRenderedComponent<ProfileEditorForm> cut) => cut.Find("form").Submit();

    [Fact]
    public void RendersTheAssignmentBlockAboveProviderAndModel()
    {
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor);

        cut.Find("[data-testid='model-assignment']");
        Assert.Contains("Automatic by role", cut.Markup);
        Assert.Contains("Manual", cut.Find("[data-testid='model-assignment-mode']").TextContent);
        Assert.True(cut.Markup.IndexOf("model-assignment", StringComparison.Ordinal)
                    < cut.Markup.IndexOf("provider-select", StringComparison.Ordinal));
    }

    [Fact]
    public void Automatic_ShowsRoleDomainAndHint_AndDisablesProviderAndModel()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer);

        Assert.True(Disabled(cut, "[data-testid='provider-select']"));
        Assert.True(Disabled(cut, "[data-testid='model-selector-trigger']"));
        Assert.Contains("field-help", cut.Find("[data-testid='model-assignment-hint']").ClassList);
        Assert.Contains("model matrix", cut.Find("[data-testid='model-assignment-hint']").TextContent);
        Assert.Contains("avoid the executor's model", cut.Find("[data-testid='model-assignment-hint']").TextContent);
        Assert.Contains(ModelSlot.FactReviewer.Describe(), cut.Find("[data-testid='model-slot-help']").TextContent);
        Assert.Equal("From crew (default)", cut.Find("[data-testid='model-domain-select'] option").TextContent);
    }

    [Fact]
    public void Automatic_ShowsTheMatrixPrimaryOfRoleAndDomain()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.DomainReviewer, ModelDomain.Legal, provider: "claude-cli", model: "claude-opus-latest");

        var primary = ModelMatrix.Primary(ModelSlot.DomainReviewer, ModelDomain.Legal);
        Assert.Equal(primary.Provider, Value(cut, "provider-select"));
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);
    }

    [Fact]
    public void Automatic_WithoutDomain_UsesTheGeneralPrimary()
    {
        var cut = Render(ModelActorKind.Advisor, ModelSlot.DomainExpertAdvisor, domain: null);

        var primary = ModelMatrix.Primary(ModelSlot.DomainExpertAdvisor, ModelDomain.General);
        Assert.Equal(primary.Provider, Value(cut, "provider-select"));
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);
    }

    [Fact]
    public void SwitchingTheRole_UpdatesTheDisplayedPrimary()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer);

        cut.Find("[data-testid='model-slot-select']").Change(nameof(ModelSlot.StyleReviewer));

        var primary = ModelMatrix.Primary(ModelSlot.StyleReviewer);
        Assert.Equal(primary.Provider, Value(cut, "provider-select"));
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);
        Assert.Contains(ModelSlot.StyleReviewer.Describe(), cut.Find("[data-testid='model-slot-help']").TextContent);
    }

    [Fact]
    public void SwitchingTheDomain_UpdatesTheDisplayedPrimary()
    {
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor);

        cut.Find("[data-testid='model-domain-select']").Change(nameof(ModelDomain.Technical));

        var primary = ModelMatrix.Primary(ModelSlot.Executor, ModelDomain.Technical);
        Assert.Contains(primary.Model, cut.Find("[data-testid='model-selector-trigger']").TextContent);
    }

    [Fact]
    public void Manual_EnablesProviderAndModelPickers_AndHidesRoleAndDomain()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer);

        SelectManual(cut);

        Assert.False(Disabled(cut, "[data-testid='provider-select']"));
        Assert.False(Disabled(cut, "[data-testid='model-selector-trigger']"));
        Assert.Empty(cut.FindAll("[data-testid='model-slot-select']"));
        Assert.Empty(cut.FindAll("[data-testid='model-domain-select']"));
    }

    [Fact]
    public void ExistingProfileWithoutSlot_OpensInManual()
    {
        var cut = Render(ModelActorKind.Reviewer, slot: null);

        Assert.True(cut.Find("[data-testid='model-assignment-manual']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='model-assignment-automatic']").HasAttribute("checked"));
        Assert.False(Disabled(cut, "[data-testid='provider-select']"));
        Assert.Empty(cut.FindAll("[data-testid='model-slot-select']"));
    }

    [Fact]
    public void ExistingProfileWithSlot_OpensInAutomatic_WithStoredRoleAndDomain()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.StyleReviewer, ModelDomain.Marketing);

        Assert.True(cut.Find("[data-testid='model-assignment-automatic']").HasAttribute("checked"));
        Assert.Equal("StyleReviewer", Value(cut, "model-slot-select"));
        Assert.Equal("Marketing", Value(cut, "model-domain-select"));
    }

    [Theory]
    [InlineData(ModelActorKind.Executor, ModelSlot.Executor)]
    [InlineData(ModelActorKind.Reviewer, ModelSlot.FactReviewer)]
    [InlineData(ModelActorKind.Advisor, ModelSlot.StrategicAdvisor)]
    public void NewProfile_DefaultsToAutomaticWithTheFirstSlotOfItsKind(ModelActorKind kind, ModelSlot expected)
    {
        var cut = Render(kind, isNew: true, provider: "", model: "");

        Assert.True(cut.Find("[data-testid='model-assignment-automatic']").HasAttribute("checked"));
        Assert.Equal(expected.ToString(), Value(cut, "model-slot-select"));
        Assert.Equal(ModelMatrix.Primary(expected).Provider, Value(cut, "provider-select"));
    }

    [Theory]
    [InlineData(AdvisorMode.Strategic, ModelSlot.StrategicAdvisor)]
    [InlineData(AdvisorMode.Critical, ModelSlot.CriticalAdvisor)]
    [InlineData(AdvisorMode.DevilsAdvocate, ModelSlot.CriticalAdvisor)]
    [InlineData(AdvisorMode.DomainExpert, ModelSlot.DomainExpertAdvisor)]
    public void NewAdvisor_DefaultSlotFollowsTheSelectedMode(AdvisorMode mode, ModelSlot expected)
    {
        var cut = Render(ModelActorKind.Advisor, isNew: true, provider: "", model: "", mode: mode);

        Assert.Equal(expected.ToString(), Value(cut, "model-slot-select"));
    }

    [Fact]
    public void NewAdvisor_ChangingTheModeBeforeTouchingTheRole_MovesTheSlot()
    {
        var cut = Render(ModelActorKind.Advisor, isNew: true, provider: "", model: "", mode: AdvisorMode.Strategic);

        cut.SetParametersAndRender(p => p.Add(c => c.SelectedMode, AdvisorMode.DomainExpert));

        Assert.Equal("DomainExpertAdvisor", Value(cut, "model-slot-select"));
    }

    [Fact]
    public void NewAdvisor_ChangingTheModeAfterPickingARole_KeepsThePickedRole()
    {
        var cut = Render(ModelActorKind.Advisor, isNew: true, provider: "", model: "", mode: AdvisorMode.Strategic);
        cut.Find("[data-testid='model-slot-select']").Change(nameof(ModelSlot.CriticalAdvisor));

        cut.SetParametersAndRender(p => p.Add(c => c.SelectedMode, AdvisorMode.DomainExpert));

        Assert.Equal("CriticalAdvisor", Value(cut, "model-slot-select"));
    }

    [Theory]
    [InlineData(ModelActorKind.Executor, new[] { "Executor", "Composer", "Helper" })]
    [InlineData(ModelActorKind.Reviewer, new[] { "FactReviewer", "StyleReviewer", "DomainReviewer", "Helper" })]
    [InlineData(ModelActorKind.Advisor, new[] { "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor" })]
    public void RoleSelect_OffersOnlyTheSlotsAllowedForTheKind(ModelActorKind kind, string[] expected)
    {
        var cut = Render(kind, kind.AllowedSlots()[0]);

        var offered = cut.FindAll("[data-testid='model-slot-select'] option").Select(o => o.GetAttribute("value")!).ToArray();

        Assert.Equal(expected, offered);
    }

    [Fact]
    public void ASlotThatIsInvalidForTheKind_IsNeverOffered_AndTheProfileOpensInManual()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.Executor);

        Assert.True(cut.Find("[data-testid='model-assignment-manual']").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("[data-testid='model-slot-select']"));

        SelectAutomatic(cut);

        var offered = cut.FindAll("[data-testid='model-slot-select'] option").Select(o => o.GetAttribute("value")).ToList();
        Assert.DoesNotContain("Executor", offered);
        Assert.DoesNotContain("Composer", offered);
        Assert.DoesNotContain("StrategicAdvisor", offered);
        Assert.Equal("FactReviewer", Value(cut, "model-slot-select"));
    }

    [Fact]
    public void SaveInAutomatic_PassesSlotDomainAndThePrimaryBinding()
    {
        ProfileEditorForm.ProfileFormValues? saved = null;
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer, onSave: v => saved = v);
        cut.Find("[data-testid='model-slot-select']").Change(nameof(ModelSlot.DomainReviewer));
        cut.Find("[data-testid='model-domain-select']").Change(nameof(ModelDomain.Legal));

        Submit(cut);

        Assert.NotNull(saved);
        var primary = ModelMatrix.Primary(ModelSlot.DomainReviewer, ModelDomain.Legal);
        Assert.Equal(ModelSlot.DomainReviewer, saved!.ModelSlot);
        Assert.Equal(ModelDomain.Legal, saved.ModelDomain);
        Assert.Equal(primary.Provider, saved.Provider);
        Assert.Equal(primary.Model, saved.Model);
    }

    [Fact]
    public void SaveInAutomatic_WithFromCrewDomain_PassesANullDomain()
    {
        ProfileEditorForm.ProfileFormValues? saved = null;
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor, ModelDomain.Legal, onSave: v => saved = v);
        cut.Find("[data-testid='model-domain-select']").Change("");

        Submit(cut);

        Assert.NotNull(saved);
        Assert.Equal(ModelSlot.Executor, saved!.ModelSlot);
        Assert.Null(saved.ModelDomain);
        var primary = ModelMatrix.Primary(ModelSlot.Executor);
        Assert.Equal(primary.Model, saved.Model);
    }

    [Fact]
    public void SaveInManual_PassesNullSlotAndDomain_AndKeepsTheChosenBinding()
    {
        ProfileEditorForm.ProfileFormValues? saved = null;
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.StyleReviewer, ModelDomain.Legal, onSave: v => saved = v);
        SelectManual(cut);
        cut.Find("[data-testid='provider-select']").Change("claude-cli");

        Submit(cut);

        Assert.NotNull(saved);
        Assert.Null(saved!.ModelSlot);
        Assert.Null(saved.ModelDomain);
        Assert.Equal("claude-cli", saved.Provider);
    }

    [Fact]
    public void SaveOfAnExistingManualProfile_KeepsItManual()
    {
        ProfileEditorForm.ProfileFormValues? saved = null;
        var cut = Render(ModelActorKind.Executor, slot: null, provider: "claude-cli", model: "claude-opus-latest", onSave: v => saved = v);

        Submit(cut);

        Assert.NotNull(saved);
        Assert.Null(saved!.ModelSlot);
        Assert.Null(saved.ModelDomain);
        Assert.Equal("claude-opus-latest", saved.Model);
    }

    [Fact]
    public void SaveInAutomatic_NeverAsksAboutAModelMissingFromTheCatalogue()
    {
        Services.AddSingleton<IModelCatalog>(new ResolvingModelCatalog());
        ProfileEditorForm.ProfileFormValues? saved = null;
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor, onSave: v => saved = v);

        Submit(cut);

        Assert.NotNull(saved);
        Assert.Empty(cut.FindAll("[data-testid='custom-model-warning-modal']"));
    }

    [Fact]
    public void SystemProfile_KeepsTheAssignmentControlsReadOnly()
    {
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer, isSystem: true);

        Assert.True(Disabled(cut, "[data-testid='model-assignment-automatic']"));
        Assert.True(Disabled(cut, "[data-testid='model-assignment-manual']"));
        Assert.True(Disabled(cut, "[data-testid='model-slot-select']"));
        Assert.True(Disabled(cut, "[data-testid='model-domain-select']"));
        Assert.True(Disabled(cut, "[data-testid='provider-select']"));
    }

    private static bool ToolWarningShown(IRenderedComponent<ProfileEditorForm> cut) =>
        cut.FindAll("[data-testid='tool-picker-warning']").Count > 0;

    private static void WithoutAgenticToolsOn(IServiceCollection services, params string[] providers) =>
        services.AddSingleton<IProviderService>(new NoAgenticToolsProviders(providers));

    [Fact]
    public void ToolSupport_FollowsTheProviderTheAutomaticAssignmentSets_NotTheStoredOne()
    {
        WithoutAgenticToolsOn(Services, "openrouter");

        var cut = Render(ModelActorKind.Reviewer, ModelSlot.DomainReviewer, ModelDomain.Legal, provider: "claude-cli", showTools: true);

        Assert.Equal("openrouter", Value(cut, "provider-select"));
        Assert.True(ToolWarningShown(cut));
    }

    [Fact]
    public void ToolSupport_IsUpdatedWhenTheDomainChangesInAutomatic()
    {
        WithoutAgenticToolsOn(Services, "openrouter");
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.DomainReviewer, provider: "codex-cli", showTools: true);
        Assert.False(ToolWarningShown(cut));

        cut.Find("[data-testid='model-domain-select']").Change(nameof(ModelDomain.Legal));
        Assert.True(ToolWarningShown(cut));

        cut.Find("[data-testid='model-domain-select']").Change(nameof(ModelDomain.Marketing));
        Assert.False(ToolWarningShown(cut));
    }

    [Fact]
    public void ToolSupport_IsUpdatedWhenTheRoleChangesInAutomatic()
    {
        WithoutAgenticToolsOn(Services, "openrouter");
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.FactReviewer, ModelDomain.Legal, showTools: true);
        Assert.False(ToolWarningShown(cut));

        cut.Find("[data-testid='model-slot-select']").Change(nameof(ModelSlot.DomainReviewer));

        Assert.True(ToolWarningShown(cut));
    }

    [Fact]
    public void ToolSupport_IsUpdatedWhenSwitchingBackToAutomatic()
    {
        WithoutAgenticToolsOn(Services, "openrouter");
        var cut = Render(ModelActorKind.Reviewer, ModelSlot.DomainReviewer, ModelDomain.Legal, showTools: true);
        SelectManual(cut);
        cut.Find("[data-testid='provider-select']").Change("claude-cli");
        Assert.False(ToolWarningShown(cut));

        SelectAutomatic(cut);

        Assert.True(ToolWarningShown(cut));
    }

    [Fact]
    public void ToolSupport_IsUpdatedWhenTheProviderChangesInManual()
    {
        WithoutAgenticToolsOn(Services, "openrouter");
        var cut = Render(ModelActorKind.Executor, slot: null, provider: "claude-cli", showTools: true);
        Assert.False(ToolWarningShown(cut));

        cut.Find("[data-testid='provider-select']").Change("openrouter");

        Assert.True(ToolWarningShown(cut));
    }

    [Theory]
    [InlineData("codex-cli", "gpt-6-luna")]
    [InlineData("codex-cli", "gpt-5.6-sol")]
    [InlineData("openrouter", "openai/gpt-4o")]
    [InlineData("openrouter", "anthropic/claude-sonnet-4")]
    public void Manual_ShowsWhereARewrittenBindingWillBeSaved(string provider, string model)
    {
        var cut = Render(ModelActorKind.Executor, slot: null, provider: provider, model: model);

        var notice = cut.Find("[data-testid='cli-route-notice']").TextContent;
        Assert.Equal(ExpectedNotice(provider, model), notice);
    }

    [Fact]
    public void Manual_ShowsNoNoticeForABindingThatIsKept()
    {
        var cut = Render(ModelActorKind.Executor, slot: null, provider: "claude-cli", model: "claude-opus-latest");

        Assert.Empty(cut.FindAll("[data-testid='cli-route-notice']"));
    }

    [Fact]
    public void Manual_KeepsTheRewrittenModelSelectable()
    {
        var cut = Render(ModelActorKind.Executor, slot: null, provider: "codex-cli", model: "gpt-6-luna");

        Assert.Equal("gpt-6-luna", cut.Find("[data-testid='model-selector-custom-input']").GetAttribute("value"));
    }

    [Fact]
    public void Automatic_ShowsNoRewriteNotice()
    {
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor, provider: "codex-cli", model: "gpt-6-luna");

        Assert.Empty(cut.FindAll("[data-testid='cli-route-notice']"));
    }

    [Fact]
    public void SwitchingToManual_ShowsTheNoticeOnlyWhileTheChosenBindingIsRewritten()
    {
        var cut = Render(ModelActorKind.Executor, ModelSlot.Executor);
        SelectManual(cut);
        Assert.Empty(cut.FindAll("[data-testid='cli-route-notice']"));

        cut.Find("[data-testid='provider-select']").Change("openrouter");
        Assert.Contains("claude-cli/", cut.Find("[data-testid='cli-route-notice']").TextContent);

        cut.Find("[data-testid='provider-select']").Change("claude-cli");
        Assert.Empty(cut.FindAll("[data-testid='cli-route-notice']"));
    }

    private static string ExpectedNotice(string provider, string model)
    {
        var target = CliRoute.Normalize(provider, model);
        Assert.NotEqual(new ModelCandidate(provider, model), target);
        return $"This model will be saved as {target.Provider}/{target.Model} (CLI route / retired model).";
    }

    private sealed class NoTools : IToolDefinitionService
    {
        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ToolDefinition?>(null);
        public Task SaveAsync(ToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoAgenticToolsProviders(params string[] withoutAgenticTools) : IProviderService
    {
        private readonly FakeProviderService _inner = new();

        public async Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => await _inner.ListAsync(includeInactive, ct);

        public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            var provider = await _inner.GetByNameAsync(name, ct);
            if (provider is null || !withoutAgenticTools.Contains(name))
                return provider;
            var settings = new Dictionary<string, JsonElement>(provider.Settings)
            {
                [ProviderSettingsKeys.SupportsAgenticTools] = JsonDocument.Parse("false").RootElement.Clone(),
            };
            return provider with { Settings = settings };
        }

        public Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default) => _inner.CreateCustomAsync(provider, ct);
        public Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default) => _inner.UpdateCustomAsync(name, provider, ct);
        public Task DeleteCustomAsync(string name, CancellationToken ct = default) => _inner.DeleteCustomAsync(name, ct);
        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default) => _inner.SetActiveAsync(name, isActive, ct);
        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default) => _inner.TestConnectionAsync(name, ct);
    }
}
