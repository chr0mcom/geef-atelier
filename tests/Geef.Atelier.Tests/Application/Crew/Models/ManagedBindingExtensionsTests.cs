using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Tests.Application.Crew.Models;

public sealed class ManagedBindingExtensionsTests
{
    [Fact]
    public void ManualReviewerOnAPaidClaudeRouteIsRerouted()
    {
        var saved = new ReviewerProfile("r", "R", "d", "p", "openrouter", "anthropic/claude-opus-5", 8000, false).ForSave();

        Assert.Equal(("claude-cli", "claude-opus-latest", (ModelSlot?)null), (saved.Provider, saved.Model, saved.ModelSlot));
    }

    [Fact]
    public void LearningExtractBindingFollowsTheCliRoute()
    {
        var profile = new FinalizerProfile("l", "L", "d", FinalizerType.LearningExtract,
            new Dictionary<string, string> { ["provider"] = "openrouter", ["model"] = "openai/gpt-5.6-sol", ["minIterations"] = "2" }, false);

        var saved = profile.ForSave();

        Assert.Equal(("codex-cli", "gpt-sol-latest", "2"), (saved.Settings["provider"], saved.Settings["model"], saved.Settings["minIterations"]));
    }

    [Fact]
    public void GroundingMarkerMustBeTheRefinerSlot()
    {
        var profile = new GroundingProviderProfile("g", "G", "d", "tavily",
            new Dictionary<string, string>
            {
                [GroundingProviderProfile.KeyRefinementProvider] = "claude-cli",
                [GroundingProviderProfile.KeyRefinementModel] = "claude-sonnet-latest",
                [GroundingProviderProfile.KeyModelSlot] = "Executor",
            }, false);

        var ex = Assert.Throws<InvalidOperationException>(() => profile.ForSave());
        Assert.Contains("grounding refiner", ex.Message);
    }

    [Fact]
    public void ManagedReviewerKeepsItsSlotAndGetsACandidate()
    {
        var saved = new ReviewerProfile("r", "R", "d", "p", "xai", "grok-4.5", 8000, false, ModelSlot: ModelSlot.StyleReviewer).ForSave();

        Assert.Equal((ModelSlot.StyleReviewer, "codex-cli", "gpt-sol-latest"), (saved.ModelSlot!.Value, saved.Provider, saved.Model));
    }

    [Fact]
    public void SlotOfAnotherActorKindIsRejected()
    {
        var reviewer = new ReviewerProfile("r", "R", "d", "p", "codex-cli", "gpt-6.2-sol", 8000, false, ModelSlot: ModelSlot.Executor);
        var executor = new ExecutorProfile("e", "E", "d", "p", "codex-cli", "gpt-6.2-sol", 8000, false, ModelSlot: ModelSlot.FactReviewer);
        var advisor = new AdvisorProfile("a", "A", "d", "p", "codex-cli", "gpt-6.2-sol", 8000, AdvisorMode.Strategic, AdvisorTrigger.BeforeFirstExecution, false, ModelSlot: ModelSlot.Helper);

        Assert.Throws<InvalidOperationException>(() => reviewer.ForSave());
        Assert.Throws<InvalidOperationException>(() => executor.ForSave());
        Assert.Throws<InvalidOperationException>(() => advisor.ForSave());
    }

    [Fact]
    public void ManagedExecutorKeepsADomainOverride()
    {
        var saved = new ExecutorProfile("e", "E", "d", "p", "", "", 32000, false, ModelSlot: ModelSlot.Executor, ModelDomain: ModelDomain.Technical).ForSave();

        Assert.Equal(("claude-cli", "claude-fable-latest", (ModelDomain?)ModelDomain.Technical), (saved.Provider, saved.Model, saved.ModelDomain));
    }

    [Fact]
    public void GroundingRefinerFollowsTheMarker()
    {
        var managed = Grounding(new() { ["refinementProvider"] = "deepseek", ["refinementModel"] = "deepseek/deepseek-v4-flash", ["modelSlot"] = "GroundingRefiner" }).ForSave();
        var manual = Grounding(new() { ["refinementProvider"] = "openrouter", ["refinementModel"] = "anthropic/claude-haiku-4.5", ["modelSlot"] = "bogus" }).ForSave();
        var none = Grounding(new() { ["Tier"] = "basic" }).ForSave();

        Assert.Equal("claude-sonnet-latest", managed.ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
        Assert.Equal(ModelSlot.GroundingRefiner, managed.RefinementModelSlot);
        Assert.Equal(("claude-cli", "claude-haiku-latest"), (manual.ProviderSettings[GroundingProviderProfile.KeyRefinementProvider], manual.ProviderSettings[GroundingProviderProfile.KeyRefinementModel]));
        Assert.False(manual.ProviderSettings.ContainsKey(GroundingProviderProfile.KeyModelSlot));
        Assert.Single(none.ProviderSettings);
    }

    [Fact]
    public void TransformFollowsTheMarkerAndRejectsNonTransformSlots()
    {
        var managed = Transform(new() { ["Provider"] = "codex-cli", ["Model"] = "gpt-5.6-sol", ["ModelSlot"] = "TransformRewrite" }).ForSave();
        var manual = Transform(new() { ["Provider"] = "openrouter", ["Model"] = "openai/gpt-5.6-sol" }).ForSave();
        var empty = Transform([]).ForSave();

        Assert.Equal(("codex-cli", "gpt-sol-latest"), (managed.Settings["Provider"], managed.Settings["Model"]));
        Assert.Equal(("codex-cli", "gpt-sol-latest"), (manual.Settings["Provider"], manual.Settings["Model"]));
        Assert.Empty(empty.Settings);
        Assert.Throws<InvalidOperationException>(() => Transform(new() { ["ModelSlot"] = "FactReviewer" }).ForSave());

        var export = new FinalizerProfile("x", "X", "d", FinalizerType.FileExport, new() { ["Provider"] = "openrouter", ["Model"] = "anthropic/claude-opus-5" }, false);
        Assert.Same(export, export.ForSave());
    }

    private static GroundingProviderProfile Grounding(Dictionary<string, string> settings) =>
        new("g", "G", "d", "tavily", settings, false);

    private static FinalizerProfile Transform(Dictionary<string, string> settings) =>
        new("t", "T", "d", FinalizerType.Transform, settings, false);
}
