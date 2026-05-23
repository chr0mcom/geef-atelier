using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

/// <summary>
/// Verifies SystemCrew exposes all 19 finalizer profiles as documented and
/// that the FinalizerProfiles dictionary is coherent.
/// </summary>
public sealed class SystemCrewFinalizerConstantsTests
{
    [Fact]
    public void FinalizerProfiles_Contains19Entries()
    {
        Assert.Equal(19, SystemCrew.FinalizerProfiles.Count);
    }

    [Theory]
    [InlineData("export-markdown", FinalizerType.FileExport)]
    [InlineData("export-html",     FinalizerType.FileExport)]
    [InlineData("export-pdf",      FinalizerType.FileExport)]
    [InlineData("export-docx",     FinalizerType.FileExport)]
    [InlineData("export-txt",      FinalizerType.FileExport)]
    [InlineData("export-json",     FinalizerType.FileExport)]
    [InlineData("add-front-matter",    FinalizerType.MetadataEnrich)]
    [InlineData("add-word-count-footer", FinalizerType.MetadataEnrich)]
    [InlineData("add-reading-level",   FinalizerType.MetadataEnrich)]
    [InlineData("webhook-sink",        FinalizerType.ExternalSink)]
    [InlineData("email-sink",          FinalizerType.ExternalSink)]
    [InlineData("anti-ai-voice",       FinalizerType.Transform)]
    [InlineData("tone-formalization",  FinalizerType.Transform)]
    [InlineData("tone-casual",         FinalizerType.Transform)]
    public void FinalizerProfiles_NamedProfile_ExistsWithCorrectType(string name, FinalizerType expectedType)
    {
        Assert.True(SystemCrew.FinalizerProfiles.ContainsKey(name), $"Profile '{name}' not found");
        Assert.Equal(expectedType, SystemCrew.FinalizerProfiles[name].FinalizerType);
    }

    [Fact]
    public void AllSystemFinalizerProfiles_HaveIsSystemTrue()
    {
        Assert.All(SystemCrew.FinalizerProfiles.Values, p => Assert.True(p.IsSystem));
    }

    [Fact]
    public void AllSystemFinalizerProfiles_HaveNonEmptyDisplayName()
    {
        Assert.All(SystemCrew.FinalizerProfiles.Values, p =>
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
    }

    [Fact]
    public void IsSystemFinalizerName_RecognizesAllSystemFinalizers()
    {
        foreach (var name in SystemCrew.FinalizerProfiles.Keys)
            Assert.True(SystemCrew.IsSystemFinalizerName(name), $"'{name}' not recognized");
    }

    [Fact]
    public void IsSystemFinalizerName_ReturnsFalseForCustomNames()
    {
        Assert.False(SystemCrew.IsSystemFinalizerName("custom-my-finalizer"));
        Assert.False(SystemCrew.IsSystemFinalizerName("unknown-finalizer"));
    }

    [Fact]
    public void ExportMarkdownProfile_HasCorrectSettings()
    {
        var settings = FileExportSettings.From(SystemCrew.ExportMarkdownProfile.Settings);
        Assert.Equal("markdown", settings.Format);
    }

    [Fact]
    public void TransformProfiles_AllHaveNonEmptySystemPrompt()
    {
        var transforms = SystemCrew.FinalizerProfiles.Values
            .Where(p => p.FinalizerType == FinalizerType.Transform);

        Assert.All(transforms, p =>
        {
            var settings = TransformSettings.From(p.Settings);
            Assert.False(string.IsNullOrWhiteSpace(settings.SystemPrompt),
                $"Transform profile '{p.Name}' has empty system prompt");
        });
    }
}
