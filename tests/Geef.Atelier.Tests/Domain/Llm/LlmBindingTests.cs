using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Tests.Domain.Llm;

public sealed class LlmBindingTests
{
    [Fact]
    public void Default_CreatesBindingWithNullTemperature()
    {
        var binding = LlmBinding.Default("codex-cli", "gpt-5.5", 4096);

        Assert.Equal("codex-cli", binding.Provider);
        Assert.Equal("gpt-5.5", binding.Model);
        Assert.Equal(4096, binding.MaxTokens);
        Assert.Null(binding.Temperature);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new LlmBinding("openrouter", "gpt-4o", 2048, 0.7);
        var b = new LlmBinding("openrouter", "gpt-4o", 2048, 0.7);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentProvider_AreNotEqual()
    {
        var a = new LlmBinding("openrouter", "gpt-4o", 2048, null);
        var b = new LlmBinding("codex-cli", "gpt-4o", 2048, null);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Temperature_StoresExactValue()
    {
        var binding = new LlmBinding("openrouter", "gpt-4o", 1024, 0.3);

        Assert.Equal(0.3, binding.Temperature);
    }

    [Fact]
    public void Temperature_ZeroValue_IsStoredAsZeroNotNull()
    {
        var binding = new LlmBinding("codex-cli", "gpt-5.5", 4096, 0.0);

        Assert.NotNull(binding.Temperature);
        Assert.Equal(0.0, binding.Temperature!.Value);
    }
}
