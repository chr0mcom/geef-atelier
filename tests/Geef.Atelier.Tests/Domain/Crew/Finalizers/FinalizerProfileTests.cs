using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

public sealed class FinalizerProfileTests
{
    [Fact]
    public void FinalizerProfile_WithSameValues_HaveSameFields()
    {
        var a = new FinalizerProfile("export-md", "Export MD", "desc",
            FinalizerType.FileExport, new() { ["Format"] = "markdown" }, true);
        var b = new FinalizerProfile("export-md", "Export MD", "desc",
            FinalizerType.FileExport, new() { ["Format"] = "markdown" }, true);

        // Dictionary<string,string> uses reference equality in records — compare fields individually
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.DisplayName, b.DisplayName);
        Assert.Equal(a.FinalizerType, b.FinalizerType);
        Assert.Equal(a.IsSystem, b.IsSystem);
        Assert.Equal(a.Settings["Format"], b.Settings["Format"]);
    }

    [Fact]
    public void FinalizerProfile_WithDifferentName_AreNotEqual()
    {
        var a = new FinalizerProfile("export-md", "Export MD", "desc",
            FinalizerType.FileExport, new() { ["Format"] = "markdown" }, true);
        var b = a with { Name = "export-html" };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FinalizerProfile_CustomProfile_HasCustomPrefix()
    {
        var profile = new FinalizerProfile(
            "custom-my-finalizer", "My Finalizer", "desc",
            FinalizerType.Transform, [], false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.StartsWith("custom-", profile.Name);
        Assert.False(profile.IsSystem);
    }

    [Fact]
    public void FinalizerProfile_SystemProfile_HasCreatedAtNull()
    {
        var profile = new FinalizerProfile("export-md", "Export MD", "desc",
            FinalizerType.FileExport, new() { ["Format"] = "markdown" }, true);

        Assert.Null(profile.CreatedAt);
        Assert.Null(profile.UpdatedAt);
    }

    [Theory]
    [InlineData(FinalizerType.FileExport)]
    [InlineData(FinalizerType.MetadataEnrich)]
    [InlineData(FinalizerType.ExternalSink)]
    [InlineData(FinalizerType.Transform)]
    public void FinalizerType_AllValues_AreDistinct(FinalizerType type)
    {
        var profile = new FinalizerProfile("test", "Test", "desc", type, [], false);
        Assert.Equal(type, profile.FinalizerType);
    }
}
