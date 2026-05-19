using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

public sealed class MetadataEnrichFinalizerExecutorTests
{
    private readonly MetadataEnrichFinalizerExecutor _executor =
        new(NullLogger<MetadataEnrichFinalizerExecutor>.Instance);

    private static FinalizerProfile ProfileWith(string enricherType) => new(
        Name: "enrich-test",
        DisplayName: "Enrich Test",
        Description: "test",
        FinalizerType: FinalizerType.MetadataEnrich,
        Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = enricherType },
        IsSystem: true);

    private static FinalizerExecutionContext MakeContext(string text = "# Hello\n\nThis is a test document with some words.") =>
        new(
            RunId: Guid.NewGuid(),
            TemplateName: "test-template",
            FinalText: text,
            CurrentText: text,
            RunCompletedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Execute_FrontMatter_PrependsFrontMatterBlock()
    {
        var result = await _executor.ExecuteAsync(
            ProfileWith(MetadataEnrichSettings.FrontMatter), MakeContext(), default);

        Assert.NotNull(result.UpdatedText);
        Assert.StartsWith("---\n", result.UpdatedText);
        Assert.Contains("title:", result.UpdatedText);
        Assert.Contains("word_count:", result.UpdatedText);
        Assert.Contains("generated_at:", result.UpdatedText);
    }

    [Fact]
    public async Task Execute_FrontMatter_PreservesOriginalContent()
    {
        var result = await _executor.ExecuteAsync(
            ProfileWith(MetadataEnrichSettings.FrontMatter), MakeContext(), default);

        Assert.Contains("Hello", result.UpdatedText);
        Assert.Contains("test document", result.UpdatedText);
    }

    [Fact]
    public async Task Execute_FrontMatter_ReplacesExistingFrontMatter()
    {
        var withExistingFrontMatter = "---\ntitle: \"Old\"\n---\n\n# Content";
        var result = await _executor.ExecuteAsync(
            ProfileWith(MetadataEnrichSettings.FrontMatter), MakeContext(withExistingFrontMatter), default);

        // Old front matter should be replaced, not doubled
        Assert.Single(result.UpdatedText!.Split("---"), s => s.Contains("title:"));
    }

    [Fact]
    public async Task Execute_WordCountFooter_AppendsFooterWithCount()
    {
        var result = await _executor.ExecuteAsync(
            ProfileWith(MetadataEnrichSettings.WordCountFooter), MakeContext(), default);

        Assert.NotNull(result.UpdatedText);
        Assert.Contains("words", result.UpdatedText);
        Assert.Contains("min read", result.UpdatedText);
        // Footer is at the end
        Assert.EndsWith("min read*", result.UpdatedText!.TrimEnd());
    }

    [Fact]
    public async Task Execute_ReadingLevel_AppendsReadingLevelFooter()
    {
        var longText = string.Join(" ", Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog.", 30));
        var result = await _executor.ExecuteAsync(
            ProfileWith(MetadataEnrichSettings.ReadingLevel), MakeContext(longText), default);

        Assert.NotNull(result.UpdatedText);
        Assert.Contains("Estimated reading level:", result.UpdatedText);
    }

    [Fact]
    public async Task Execute_AllEnrichers_ReturnNullArtifactOnSuccess()
    {
        foreach (var enricher in new[]
        {
            MetadataEnrichSettings.FrontMatter,
            MetadataEnrichSettings.WordCountFooter,
            MetadataEnrichSettings.ReadingLevel,
        })
        {
            var result = await _executor.ExecuteAsync(
                ProfileWith(enricher), MakeContext(), default);

            Assert.Null(result.Artifact);
            Assert.Null(result.CostEur);
        }
    }

    [Fact]
    public async Task Execute_InvalidEnricherType_ProducesStatusArtifact()
    {
        var profile = new FinalizerProfile(
            Name: "enrich-bad",
            DisplayName: "Bad Enricher",
            Description: "test",
            FinalizerType: FinalizerType.MetadataEnrich,
            Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = "unknown-enricher" },
            IsSystem: false);

        var result = await _executor.ExecuteAsync(profile, MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact.ArtifactType);
    }
}
