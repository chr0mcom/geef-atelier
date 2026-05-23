using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

public sealed class SettingsRecordTests
{
    [Theory]
    [InlineData("markdown", "markdown")]
    [InlineData("html", "html")]
    [InlineData("pdf", "pdf")]
    [InlineData("docx", "docx")]
    [InlineData("txt", "txt")]
    [InlineData("json", "json")]
    public void FileExportSettings_RoundTrip(string format, string expected)
    {
        var original = new FileExportSettings(format);
        var dict = original.ToDict();
        var restored = FileExportSettings.From(dict);

        Assert.Equal(expected, restored.Format);
    }

    [Fact]
    public void FileExportSettings_MissingKey_DefaultsToMarkdown()
    {
        var settings = FileExportSettings.From([]);
        Assert.Equal("markdown", settings.Format);
    }

    [Theory]
    [InlineData(MetadataEnrichSettings.FrontMatter)]
    [InlineData(MetadataEnrichSettings.WordCountFooter)]
    [InlineData(MetadataEnrichSettings.ReadingLevel)]
    public void MetadataEnrichSettings_RoundTrip(string enricherType)
    {
        var original = new MetadataEnrichSettings(enricherType);
        var dict = original.ToDict();
        var restored = MetadataEnrichSettings.From(dict);

        Assert.Equal(enricherType, restored.EnricherType);
    }

    [Fact]
    public void MetadataEnrichSettings_MissingKey_DefaultsToFrontMatter()
    {
        var settings = MetadataEnrichSettings.From([]);
        Assert.Equal(MetadataEnrichSettings.FrontMatter, settings.EnricherType);
    }

    [Fact]
    public void WebhookSinkSettings_RoundTrip()
    {
        var original = new WebhookSinkSettings("https://hook.example.com", "Bearer token123", "application/json", 15);
        var dict = original.ToDict();
        var restored = WebhookSinkSettings.From(dict);

        Assert.Equal(original.Url, restored.Url);
        Assert.Equal(original.AuthHeader, restored.AuthHeader);
        Assert.Equal(original.ContentType, restored.ContentType);
        Assert.Equal(original.TimeoutSeconds, restored.TimeoutSeconds);
    }

    [Fact]
    public void WebhookSinkSettings_NullAuthHeader_OmittedFromDict()
    {
        var original = new WebhookSinkSettings("https://hook.example.com", null, "application/json", 30);
        var dict = original.ToDict();

        Assert.False(dict.ContainsKey(WebhookSinkSettings.KeyAuthHeader));
    }

    [Fact]
    public void TransformSettings_RoundTrip()
    {
        var original = new TransformSettings("You are a professional editor.", "codex-cli", "gpt-5.5", 2048);
        var dict = original.ToDict();
        var restored = TransformSettings.From(dict);

        Assert.Equal(original.SystemPrompt, restored.SystemPrompt);
        Assert.Equal(original.Provider, restored.Provider);
        Assert.Equal(original.Model, restored.Model);
        Assert.Equal(original.MaxTokens, restored.MaxTokens);
    }

    [Fact]
    public void TransformSettings_MissingKeys_UseSensibleDefaults()
    {
        var settings = TransformSettings.From([]);
        Assert.Equal(string.Empty, settings.SystemPrompt);
        Assert.Equal("codex-cli", settings.Provider);
        Assert.Equal("gpt-5.5", settings.Model);
        Assert.Equal(60000, settings.MaxTokens);
    }

    [Fact]
    public void FinalizerType_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)FinalizerType.FileExport);
        Assert.Equal(1, (int)FinalizerType.MetadataEnrich);
        Assert.Equal(2, (int)FinalizerType.ExternalSink);
        Assert.Equal(3, (int)FinalizerType.Transform);
    }
}
