using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

public sealed class RunArtifactTests
{
    [Fact]
    public void RunArtifact_File_HasExpectedFields()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var artifact = new RunArtifact
        {
            Id = id,
            RunId = runId,
            FinalizerProfileName = "export-markdown",
            ArtifactType = ArtifactType.File,
            Filename = "document.md",
            ContentType = "text/markdown",
            SizeBytes = 1234,
            StorageUri = "/app/exports/abc/document.md",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(id, artifact.Id);
        Assert.Equal(runId, artifact.RunId);
        Assert.Equal("export-markdown", artifact.FinalizerProfileName);
        Assert.Equal(ArtifactType.File, artifact.ArtifactType);
        Assert.Equal("document.md", artifact.Filename);
        Assert.Equal("text/markdown", artifact.ContentType);
        Assert.Equal(1234, artifact.SizeBytes);
        Assert.Null(artifact.StatusMessage);
    }

    [Fact]
    public void RunArtifact_Status_HasStatusMessage()
    {
        var artifact = new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            FinalizerProfileName = "export-pdf",
            ArtifactType = ArtifactType.Status,
            StorageUri = "error",
            StatusMessage = "Export failed: disk full",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(ArtifactType.Status, artifact.ArtifactType);
        Assert.Equal("Export failed: disk full", artifact.StatusMessage);
        Assert.Null(artifact.Filename);
        Assert.Null(artifact.ContentType);
        Assert.Null(artifact.SizeBytes);
    }

    [Fact]
    public void RunArtifact_Url_StoresUri()
    {
        var artifact = new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            FinalizerProfileName = "webhook-sink",
            ArtifactType = ArtifactType.Url,
            StorageUri = "https://example.com/posts/123",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(ArtifactType.Url, artifact.ArtifactType);
        Assert.Equal("https://example.com/posts/123", artifact.StorageUri);
    }

    [Fact]
    public void ArtifactType_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)ArtifactType.File);
        Assert.Equal(1, (int)ArtifactType.Url);
        Assert.Equal(2, (int)ArtifactType.Status);
    }
}
