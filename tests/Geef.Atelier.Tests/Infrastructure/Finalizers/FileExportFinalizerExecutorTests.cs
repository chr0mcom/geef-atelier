using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

public sealed class FileExportFinalizerExecutorTests : IDisposable
{
    private readonly string _exportDir = Path.Combine(Path.GetTempPath(), $"geef-test-{Guid.NewGuid():N}");
    private readonly FileExportFinalizerExecutor _executor;

    public FileExportFinalizerExecutorTests()
    {
        Directory.CreateDirectory(_exportDir);
        var options = Options.Create(new FinalizerOptions { ExportPath = _exportDir });
        _executor = new FileExportFinalizerExecutor(options, NullLogger<FileExportFinalizerExecutor>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_exportDir))
            Directory.Delete(_exportDir, recursive: true);
    }

    private static FinalizerProfile ProfileWith(string format) => new(
        Name: $"export-{format}",
        DisplayName: $"Export {format}",
        Description: "test",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = format },
        IsSystem: true);

    private static FinalizerExecutionContext MakeContext(Guid runId) => new(
        RunId: runId,
        TemplateName: "test-template",
        FinalText: "# Hello World\n\nThis is a **test** document.",
        CurrentText: "# Hello World\n\nThis is a **test** document.",
        RunCompletedAt: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("markdown", ".md", "text/markdown")]
    [InlineData("html", ".html", "text/html")]
    [InlineData("txt", ".txt", "text/plain")]
    [InlineData("json", ".json", "application/json")]
    public async Task Execute_TextFormats_ProducesFileArtifact(
        string format, string extension, string mimePrefix)
    {
        var runId = Guid.NewGuid();
        var result = await _executor.ExecuteAsync(
            ProfileWith(format), MakeContext(runId), default);

        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.File, result.Artifact.ArtifactType);
        Assert.EndsWith(extension, result.Artifact.Filename);
        Assert.StartsWith(mimePrefix, result.Artifact.ContentType);
        Assert.True(result.Artifact.SizeBytes > 0);
        Assert.True(File.Exists(result.Artifact.StorageUri),
            $"File not found: {result.Artifact.StorageUri}");
    }

    [Fact]
    public async Task Execute_MarkdownFormat_WritesReadableMarkdown()
    {
        var runId = Guid.NewGuid();
        var result = await _executor.ExecuteAsync(
            ProfileWith("markdown"), MakeContext(runId), default);

        var content = await File.ReadAllTextAsync(result.Artifact!.StorageUri);
        Assert.Contains("Hello World", content);
    }

    [Fact]
    public async Task Execute_HtmlFormat_ContainsDoctypeAndBody()
    {
        var runId = Guid.NewGuid();
        var result = await _executor.ExecuteAsync(
            ProfileWith("html"), MakeContext(runId), default);

        var content = await File.ReadAllTextAsync(result.Artifact!.StorageUri);
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("<body>", content);
        Assert.Contains("Hello World", content);
    }

    [Fact]
    public async Task Execute_PdfFormat_ProducesValidPdfMagicBytes()
    {
        var runId = Guid.NewGuid();
        var result = await _executor.ExecuteAsync(
            ProfileWith("pdf"), MakeContext(runId), default);

        Assert.NotNull(result.Artifact);
        Assert.Equal("application/pdf", result.Artifact.ContentType);
        var bytes = await File.ReadAllBytesAsync(result.Artifact.StorageUri);
        // PDF magic number: %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public async Task Execute_DocxFormat_ProducesZipContainer()
    {
        var runId = Guid.NewGuid();
        var result = await _executor.ExecuteAsync(
            ProfileWith("docx"), MakeContext(runId), default);

        Assert.NotNull(result.Artifact);
        var bytes = await File.ReadAllBytesAsync(result.Artifact.StorageUri);
        // DOCX is a ZIP local-file-header: PK\x03\x04
        Assert.Equal(0x50, bytes[0]); // P
        Assert.Equal(0x4B, bytes[1]); // K
        Assert.Equal(0x03, bytes[2]);
        Assert.Equal(0x04, bytes[3]);
    }

    [Fact]
    public async Task Execute_ExceedsMaxFileSizeBytes_ProducesStatusArtifact()
    {
        var runId = Guid.NewGuid();
        // Restrict max to 1 byte so even markdown output exceeds the limit
        var tinyOptions = Options.Create(new FinalizerOptions { ExportPath = _exportDir, MaxFileSizeBytes = 1 });
        var restrictedExecutor = new FileExportFinalizerExecutor(tinyOptions, NullLogger<FileExportFinalizerExecutor>.Instance);

        var result = await restrictedExecutor.ExecuteAsync(ProfileWith("markdown"), MakeContext(runId), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact!.ArtifactType);
        Assert.Contains("exceeds", result.Artifact.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_CreatesRunSubdirectory()
    {
        var runId = Guid.NewGuid();
        await _executor.ExecuteAsync(ProfileWith("markdown"), MakeContext(runId), default);

        Assert.True(Directory.Exists(Path.Combine(_exportDir, runId.ToString("N"))));
    }
}
