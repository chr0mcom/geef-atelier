using Geef.Atelier.Core.Domain.Providers;

namespace Geef.Atelier.Tests.Domain;

public sealed class ProviderTests
{
    [Fact]
    public void ProviderType_HasCorrectValues()
    {
        Assert.Equal(0, (int)ProviderType.Http);
        Assert.Equal(1, (int)ProviderType.Cli);
    }

    [Fact]
    public void SystemProviders_ContainsAllExpectedProviders()
    {
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("openrouter"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("openai-direct"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("google-ai-studio"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("groq"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("deepseek"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("ollama-local"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("azure-openai"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("openai-compatible-generic"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("claude-cli"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("codex-cli"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("gemini-cli"));
        Assert.True(SystemProviders.ProvidersByName.ContainsKey("xai"));
        Assert.Equal(12, SystemProviders.ProvidersByName.Count);
    }

    [Fact]
    public void IsSystemProviderName_ReturnsTrueForSystemProvider()
    {
        Assert.True(SystemProviders.IsSystemProviderName("openrouter"));
        Assert.False(SystemProviders.IsSystemProviderName("custom-myprovider"));
    }

    [Fact]
    public void EnsureCustomPrefix_AddsPrefix()
    {
        Assert.Equal("custom-test", SystemProviders.EnsureCustomPrefix("test"));
        Assert.Equal("custom-test", SystemProviders.EnsureCustomPrefix("custom-test"));
    }

    [Fact]
    public void HttpProviderSettings_FromSettings_DeserializesCorrectly()
    {
        var settings = SystemProviders.OpenRouter.Settings;
        var typed = HttpProviderSettings.FromSettings(settings);
        Assert.Equal("https://openrouter.ai/api/v1", typed.Endpoint);
        Assert.Equal("LLM_OPENROUTER_API_KEY", typed.ApiKeyEnv);
        Assert.Equal("/models", typed.ModelsEndpoint);
        Assert.Equal("Authorization", typed.AuthHeaderName);
        Assert.Contains("HTTP-Referer", typed.DefaultHeaders);
    }

    [Fact]
    public void CliProviderSettings_FromSettings_DeserializesCorrectly()
    {
        var settings = SystemProviders.ClaudeCli.Settings;
        var typed = CliProviderSettings.FromSettings(settings);
        Assert.Equal("claude", typed.CliKind);
        Assert.Equal("claude", typed.Binary);
        Assert.Equal(2, typed.MaxConcurrent);
        Assert.Contains("claude-opus-4-8", typed.Models);
    }

    [Fact]
    public void SystemProviders_AllHaveIsSystemTrue()
    {
        foreach (var provider in SystemProviders.ProvidersByName.Values)
            Assert.True(provider.IsSystem, $"Expected {provider.Name} to have IsSystem = true");
    }

    [Fact]
    public void SystemProviders_HttpProviders_HaveHttpType()
    {
        var httpProviders = new[] { "openrouter", "openai-direct", "google-ai-studio", "groq", "deepseek", "ollama-local", "azure-openai", "openai-compatible-generic" };
        foreach (var name in httpProviders)
        {
            var p = SystemProviders.ProvidersByName[name];
            Assert.Equal(ProviderType.Http, p.Type);
        }
    }

    [Fact]
    public void SystemProviders_CliProviders_HaveCliType()
    {
        var cliProviders = new[] { "claude-cli", "codex-cli", "gemini-cli" };
        foreach (var name in cliProviders)
        {
            var p = SystemProviders.ProvidersByName[name];
            Assert.Equal(ProviderType.Cli, p.Type);
        }
    }

    [Fact]
    public void GeminiCli_Settings_DeserializesCorrectly()
    {
        var settings = SystemProviders.GeminiCli.Settings;
        var typed = CliProviderSettings.FromSettings(settings);
        Assert.Equal("gemini", typed.CliKind);
        Assert.Equal("gemini", typed.Binary);
        Assert.Contains("gemini-2-5-pro", typed.Models);
    }
}
