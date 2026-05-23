using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

public sealed class TransformSettingsTests
{
    // ── From() ──────────────────────────────────────────────────────────────────

    [Fact]
    public void From_ParsesAllFourFields()
    {
        var dict = new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = "You are an editor.",
            [TransformSettings.KeyProvider] = "openrouter",
            [TransformSettings.KeyModel] = "gpt-4o",
            [TransformSettings.KeyMaxTokens] = "2048",
        };

        var settings = TransformSettings.From(dict);

        Assert.Equal("You are an editor.", settings.SystemPrompt);
        Assert.Equal("openrouter", settings.Provider);
        Assert.Equal("gpt-4o", settings.Model);
        Assert.Equal(2048, settings.MaxTokens);
    }

    [Fact]
    public void From_WithoutTemperatureKey_TemperatureIsNull()
    {
        var dict = new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = "Prompt.",
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "4096",
        };

        var settings = TransformSettings.From(dict);

        Assert.Null(settings.Temperature);
    }

    [Fact]
    public void From_WithTemperatureKey_ParsesDecimalValue()
    {
        var dict = new Dictionary<string, string>
        {
            [TransformSettings.KeySystemPrompt] = "Prompt.",
            [TransformSettings.KeyProvider] = "codex-cli",
            [TransformSettings.KeyModel] = "gpt-5.5",
            [TransformSettings.KeyMaxTokens] = "4096",
            [TransformSettings.KeyTemperature] = "0.7",
        };

        var settings = TransformSettings.From(dict);

        Assert.NotNull(settings.Temperature);
        Assert.Equal(0.7, settings.Temperature!.Value, precision: 10);
    }

    [Fact]
    public void From_BackwardsCompatibility_MissingProviderAndModel_UsesDefaults()
    {
        var settings = TransformSettings.From([]);

        Assert.Equal("codex-cli", settings.Provider);
        Assert.Equal("gpt-5.5", settings.Model);
        Assert.Equal(60000, settings.MaxTokens);
    }

    // ── ToDict() ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToDict_WithoutTemperature_DoesNotContainTemperatureKey()
    {
        var settings = new TransformSettings("Prompt.", "codex-cli", "gpt-5.5", 4096, null);

        var dict = settings.ToDict();

        Assert.False(dict.ContainsKey(TransformSettings.KeyTemperature));
    }

    [Fact]
    public void ToDict_WithTemperature_ContainsDotSeparatedValue()
    {
        var settings = new TransformSettings("Prompt.", "codex-cli", "gpt-5.5", 4096, 0.7);

        var dict = settings.ToDict();

        Assert.True(dict.TryGetValue(TransformSettings.KeyTemperature, out var raw));
        Assert.Equal("0.7", raw);
        // Ensure dot separator, not comma (InvariantCulture)
        Assert.DoesNotContain(",", raw);
    }

    [Fact]
    public void ToDict_WithTemperature_RoundTripsCorrectly()
    {
        var original = new TransformSettings("Prompt.", "codex-cli", "gpt-5.5", 4096, 0.7);
        var dict = original.ToDict();
        var restored = TransformSettings.From(dict);

        Assert.Equal(0.7, restored.Temperature!.Value, precision: 10);
    }

    // ── Binding property ────────────────────────────────────────────────────────

    [Fact]
    public void Binding_ReturnsCorrectLlmBinding()
    {
        var settings = new TransformSettings("Prompt.", "openrouter", "gpt-4o", 2048, 0.5);

        var binding = settings.Binding;

        Assert.Equal("openrouter", binding.Provider);
        Assert.Equal("gpt-4o", binding.Model);
        Assert.Equal(2048, binding.MaxTokens);
        Assert.Equal(0.5, binding.Temperature);
    }

    [Fact]
    public void Binding_WhenNoTemperature_BindingTemperatureIsNull()
    {
        var settings = new TransformSettings("Prompt.", "codex-cli", "gpt-5.5", 4096, null);

        Assert.Null(settings.Binding.Temperature);
    }

    // ── WithBinding() ───────────────────────────────────────────────────────────

    [Fact]
    public void WithBinding_CreatesNewSettingsWithBindingValues()
    {
        var original = new TransformSettings("Original prompt.", "codex-cli", "gpt-5.5", 4096, null);
        var newBinding = new LlmBinding("openrouter", "gpt-4o", 2048, 0.8);

        var updated = original.WithBinding(newBinding);

        // Binding values transferred
        Assert.Equal("openrouter", updated.Provider);
        Assert.Equal("gpt-4o", updated.Model);
        Assert.Equal(2048, updated.MaxTokens);
        Assert.Equal(0.8, updated.Temperature);
        // Non-binding field preserved
        Assert.Equal("Original prompt.", updated.SystemPrompt);
    }

    [Fact]
    public void WithBinding_DoesNotMutateOriginal()
    {
        var original = new TransformSettings("Prompt.", "codex-cli", "gpt-5.5", 4096, null);
        var newBinding = new LlmBinding("openrouter", "gpt-4o", 512, 0.3);

        _ = original.WithBinding(newBinding);

        Assert.Equal("codex-cli", original.Provider);
        Assert.Equal("gpt-5.5", original.Model);
        Assert.Equal(4096, original.MaxTokens);
        Assert.Null(original.Temperature);
    }
}
