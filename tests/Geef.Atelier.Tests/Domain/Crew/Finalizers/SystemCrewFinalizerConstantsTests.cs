using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

/// <summary>
/// Verifies SystemCrew exposes its finalizer profiles as documented and that the FinalizerProfiles dictionary is coherent.
/// The two ExternalSink profiles were retired in stage 8c (D-089).
/// </summary>
public sealed class SystemCrewFinalizerConstantsTests
{
    [Fact]
    public void FinalizerProfiles_Contains18Entries()
    {
        Assert.Equal(18, SystemCrew.FinalizerProfiles.Count);
    }

    [Theory]
    [InlineData("webhook-sink")]
    [InlineData("email-sink")]
    public void The_retired_sink_profiles_are_gone(string name)
    {
        Assert.False(SystemCrew.FinalizerProfiles.ContainsKey(name));
    }

    [Fact]
    public void Only_ExternalSink_is_retired_and_no_system_profile_uses_it()
    {
        Assert.Equal([(FinalizerType)2], Enum.GetValues<FinalizerType>().Where(t => t.IsRetired()));
        Assert.DoesNotContain((FinalizerType)2, FinalizerTypeExtensions.Usable);
        Assert.DoesNotContain(SystemCrew.FinalizerProfiles.Values, p => p.FinalizerType.IsRetired());
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
