using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

public sealed class ModelNameNormalizerTests
{
    // ── CLI providers: prefix stripping + dot-to-dash ─────────────────────

    [Theory]
    [InlineData("claude-cli", "anthropic/claude-opus-4.7",  "claude-opus-4-7")]
    [InlineData("claude-cli", "anthropic/claude-sonnet-4.6","claude-sonnet-4-6")]
    [InlineData("claude-cli", "anthropic/claude-haiku-4.5", "claude-haiku-4-5")]
    [InlineData("claude-cli", "claude-opus-4-7",            "claude-opus-4-7")]   // already canonical
    [InlineData("claude-cli", "claude-haiku-4.5",           "claude-haiku-4-5")]  // dots only
    [InlineData("codex-cli",  "openai/gpt-5.5",             "gpt-5.5")]           // dot kept (GPT convention)
    [InlineData("codex-cli",  "gpt-5.5",                    "gpt-5.5")]           // already canonical
    [InlineData("codex-cli",  "openai/gpt-4o",              "gpt-4o")]
    [InlineData("gemini-cli", "google/gemini-2.5-pro",      "gemini-2-5-pro")]
    [InlineData("gemini-cli", "google/gemini-2.5-flash",    "gemini-2-5-flash")]
    [InlineData("gemini-cli", "gemini-2-5-pro",             "gemini-2-5-pro")]    // already canonical
    public void CliProviders_NormalizeCorrectly(string provider, string input, string expected)
    {
        Assert.Equal(expected, ModelNameNormalizer.Normalize(provider, input));
    }

    // ── HTTP providers: untouched ─────────────────────────────────────────

    [Theory]
    [InlineData("anthropic",   "claude-opus-4-5-20241022")]
    [InlineData("openai",      "gpt-4o-2024-08-06")]
    [InlineData("openrouter",  "anthropic/claude-opus-4-5")]
    [InlineData("deepseek",    "deepseek-chat")]
    [InlineData("groq",        "llama-3.3-70b-versatile")]
    [InlineData("CustomHttp",  "my-model/v2.1")]
    public void HttpProviders_ReturnModelUnchanged(string provider, string model)
    {
        Assert.Equal(model, ModelNameNormalizer.Normalize(provider, model));
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_IsCaseInsensitiveForProvider()
    {
        Assert.Equal("claude-opus-4-7", ModelNameNormalizer.Normalize("CLAUDE-CLI", "anthropic/claude-opus-4.7"));
        Assert.Equal("claude-opus-4-7", ModelNameNormalizer.Normalize("Claude-Cli", "anthropic/claude-opus-4.7"));
    }

    [Fact]
    public void Normalize_IsIdempotent_ForCliProvider()
    {
        const string canonical = "claude-opus-4-7";
        var once  = ModelNameNormalizer.Normalize("claude-cli", canonical);
        var twice = ModelNameNormalizer.Normalize("claude-cli", once);
        Assert.Equal(canonical, once);
        Assert.Equal(canonical, twice);
    }

    [Theory]
    [InlineData("claude-cli", "ANTHROPIC/Claude-Opus-4.7", "Claude-Opus-4-7")]
    public void Normalize_PrefixStrip_IsCaseInsensitive_ButPreservesModelCasing(
        string provider, string input, string expected)
    {
        Assert.Equal(expected, ModelNameNormalizer.Normalize(provider, input));
    }
}
