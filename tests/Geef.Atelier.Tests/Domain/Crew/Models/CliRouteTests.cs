using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class CliRouteTests
{
    [Theory]
    [InlineData("openrouter", "anthropic/claude-opus-5", "claude-cli", "claude-opus-latest")]
    [InlineData("openrouter", "anthropic/claude-sonnet-5", "claude-cli", "claude-sonnet-latest")]
    [InlineData("openrouter", "anthropic/claude-fable-5.2", "claude-cli", "claude-fable-latest")]
    [InlineData("openrouter", "anthropic/claude-haiku-4.5", "claude-cli", "claude-haiku-latest")]
    [InlineData("openai-direct", "claude-3-weird", "claude-cli", "claude-opus-latest")]
    [InlineData("openrouter", "openai/gpt-5.6-sol", "codex-cli", "gpt-sol-latest")]
    [InlineData("openrouter", "openai/gpt-6.2-sol", "codex-cli", "gpt-6.2-sol")]
    [InlineData("openai-direct", "gpt-6.2-sol", "codex-cli", "gpt-6.2-sol")]
    [InlineData("openai-direct", "o3-mini", "codex-cli", "gpt-astra-latest")]
    [InlineData("codex-cli", "gpt-5.6-sol", "codex-cli", "gpt-sol-latest")]
    [InlineData("codex-cli", "gpt-6-luna", "codex-cli", "gpt-astra-latest")]
    [InlineData("openrouter", "openai/gpt-4o", "codex-cli", "gpt-astra-latest")]
    [InlineData("codex-cli", "gpt-5.6-terra", "codex-cli", "gpt-terra-latest")]
    [InlineData("codex-cli", "gpt-sol-latest", "codex-cli", "gpt-sol-latest")]
    [InlineData("codex-cli", "o3", "codex-cli", "gpt-astra-latest")]
    [InlineData("openrouter", "openai/gpt-6.3-sol", "codex-cli", "gpt-6.3-sol")]
    [InlineData("codex-cli", "gpt-6.3-sol", "codex-cli", "gpt-6.3-sol")]
    [InlineData("ollama-local", "gpt-oss:20b", "ollama-local", "gpt-oss:20b")]
    [InlineData("openrouter", "openai/gpt-oss-120b", "openrouter", "openai/gpt-oss-120b")]
    [InlineData("codex-cli", "gpt-6.1-terra", "codex-cli", "gpt-6.1-terra")]
    [InlineData("claude-cli", "claude-opus-5-5", "claude-cli", "claude-opus-5-5")]
    [InlineData("xai", "grok-4.5", "xai", "grok-4.5")]
    [InlineData("google-ai-studio", "models/gemini-3.6-flash", "google-ai-studio", "models/gemini-3.6-flash")]
    [InlineData("openrouter", "google/gemini-4-neon", "openrouter", "google/gemini-4-neon")]
    public void NormalizeAppliesTheCliRouteRule(string provider, string model, string expectedProvider, string expectedModel)
    {
        Assert.Equal(new ModelCandidate(expectedProvider, expectedModel), CliRoute.Normalize(provider, model));
        Assert.Equal(provider != expectedProvider || model != expectedModel, CliRoute.IsNonCliRoute(provider, model));
    }

    [Theory]
    [InlineData("openrouter", "anthropic/claude-opus-5", VendorFamily.Anthropic)]
    [InlineData("claude-cli", "anything", VendorFamily.Anthropic)]
    [InlineData("codex-cli", "anything", VendorFamily.OpenAi)]
    [InlineData("openai-direct", "o1", VendorFamily.OpenAi)]
    [InlineData("openrouter", "google/gemini-4-neon", VendorFamily.Google)]
    [InlineData("google-ai-studio", "models/gemini-3.6-flash", VendorFamily.Google)]
    [InlineData("xai", "grok-4.5", VendorFamily.Other)]
    [InlineData("deepseek", "deepseek-v4-pro", VendorFamily.Other)]
    public void FamilyOfRecognisesVendors(string provider, string model, VendorFamily expected) =>
        Assert.Equal(expected, CliRoute.FamilyOf(provider, model));

    [Theory]
    [InlineData("claude-opus-4.8", "anthropic/claude-opus-4-8", true)]
    [InlineData("claude-opus-latest", "claude-opus-5-5", true)]
    [InlineData("claude-opus-5-5", "claude-opus-latest", true)]
    [InlineData("claude-sonnet-latest", "claude-opus-5-5", false)]
    [InlineData("claude-opus-5", "claude-opus-5-5", false)]
    [InlineData("gpt-6.2-sol", "gpt-6.2-astra", false)]
    [InlineData("gpt-6.2-sol", "openai/gpt-6.2-sol", true)]
    [InlineData("gpt-sol-latest", "gpt-6.2-sol", true)]
    [InlineData("gpt-6.2-sol", "gpt-sol-latest", true)]
    [InlineData("gpt-sol-latest", "gpt-6.2-astra", false)]
    [InlineData("gpt-astra-latest", "claude-opus-latest", false)]
    [InlineData("gpt-oss-latest", "gpt-oss-120b", false)]
    public void IsSameModelComparesAcrossSpellingsAndClaudeAliases(string a, string b, bool expected) =>
        Assert.Equal(expected, CliRoute.IsSameModel(a, b));

    [Fact]
    public void EmptyBindingsAreLeftAlone()
    {
        Assert.Equal(new ModelCandidate("", ""), CliRoute.Normalize("", ""));
        Assert.False(CliRoute.IsNonCliRoute("", ""));
    }
}
