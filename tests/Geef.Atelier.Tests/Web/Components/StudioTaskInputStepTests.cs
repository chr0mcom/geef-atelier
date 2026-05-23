using Bunit;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioTaskInputStepTests : TestContext
{
    public StudioTaskInputStepTests()
    {
        Services.AddSingleton<IModelCatalog>(new EmptyModelCatalog());
    }

    private sealed class EmptyModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public bool IsUsingFallback(string providerName) => false;
    }

    [Fact]
    public void StudioTaskInputStep_RendersTextarea()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "");
        });

        cut.Find("[data-testid='studio-task-input-step']");
        cut.Find("[data-testid='task-description-input']");
    }

    [Fact]
    public void StudioTaskInputStep_AnalyzeButton_DisabledWhenEmpty()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "");
        });

        var button = cut.Find("[data-testid='analyze-button']");
        Assert.NotNull(button.GetAttribute("disabled"));
    }

    [Fact]
    public void StudioTaskInputStep_AnalyzeButton_EnabledWhenTextEntered()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "Write a detailed product report.");
        });

        var button = cut.Find("[data-testid='analyze-button']");
        Assert.Null(button.GetAttribute("disabled"));
    }

    [Fact]
    public void StudioTaskInputStep_RendersModelSection_WithProviderAndMaxTokens()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "Task");
            p.Add(c => c.InitialProvider, "openrouter");
            p.Add(c => c.InitialModel, "anthropic/claude-opus-4-7");
            p.Add(c => c.InitialMaxTokens, 8192);
        });

        cut.Find("[data-testid='studio-model-section']");
        cut.Find("[data-testid='studio-provider-select']");
        cut.Find("[data-testid='studio-max-tokens-input']");
        cut.Find("[data-testid='studio-save-default-checkbox']");
    }

    [Fact]
    public void StudioTaskInputStep_SummaryShowsCurrentProviderAndModel()
    {
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "Task");
            p.Add(c => c.InitialProvider, "codex-cli");
            p.Add(c => c.InitialModel, "gpt-5.5");
            p.Add(c => c.InitialMaxTokens, 8192);
        });

        var summary = cut.Find("[data-testid='studio-model-summary-value']");
        Assert.Contains("codex-cli", summary.TextContent);
        Assert.Contains("gpt-5.5", summary.TextContent);
    }

    [Fact]
    public async Task StudioTaskInputStep_Analyze_EmitsSelectedChoice()
    {
        StudioModelChoice? captured = null;
        var cut = RenderComponent<StudioTaskInputStep>(p =>
        {
            p.Add(c => c.TaskDescription, "Write a report.");
            p.Add(c => c.InitialProvider, "codex-cli");
            p.Add(c => c.InitialModel, "gpt-5.5");
            p.Add(c => c.InitialMaxTokens, 12000);
            p.Add(c => c.OnAnalyze, (StudioModelChoice ch) => captured = ch);
        });

        await cut.Find("[data-testid='analyze-button']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.Equal("codex-cli", captured!.Provider);
        Assert.Equal("gpt-5.5", captured.Model);
        Assert.Equal(12000, captured.MaxTokens);
    }
}
