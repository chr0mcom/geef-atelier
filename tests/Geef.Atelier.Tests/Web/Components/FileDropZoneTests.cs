using Bunit;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components.Forms;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class FileDropZoneTests : TestContext
{
    [Fact]
    public void RendersDropZoneAndBrowseLabel()
    {
        var cut = RenderComponent<FileDropZone>();

        cut.Find("[data-testid='file-drop-zone']");
        Assert.Contains("Drop files here or click to browse", cut.Markup);
    }

    [Fact]
    public void InitiallyNoFileListOrErrors()
    {
        var cut = RenderComponent<FileDropZone>();

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='file-drop-list']"));
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='file-drop-errors']"));
    }

    [Fact]
    public void AllowedExtensions_ShownInHint()
    {
        var cut = RenderComponent<FileDropZone>(p =>
            p.Add(c => c.AllowedExtensions, new[] { ".md", ".txt" }));

        Assert.Contains(".md", cut.Markup);
        Assert.Contains(".txt", cut.Markup);
    }

    [Fact]
    public void OversizedFile_ShowsErrorAndIsExcluded()
    {
        IReadOnlyList<IBrowserFile>? captured = null;
        var cut = RenderComponent<FileDropZone>(p =>
        {
            p.Add(c => c.MaxFileSizeBytes, 100L);
            p.Add(c => c.AllowedExtensions, new[] { ".md" });
            p.Add(c => c.FilesChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(
                    this, files => captured = files));
        });

        var bigFile = new FakeBrowserFile("big.md", 1024);
        var input = cut.FindComponent<InputFile>();
        input.InvokeAsync(() =>
            input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([bigFile])));

        cut.WaitForState(() => cut.FindAll("[data-testid='file-drop-errors']").Count > 0);

        cut.Find("[data-testid='file-drop-errors']");
        Assert.Contains("exceeds", cut.Markup);
        Assert.True(captured is null || captured.Count == 0);
    }

    [Fact]
    public void UnsupportedExtension_ShowsErrorAndIsExcluded()
    {
        IReadOnlyList<IBrowserFile>? captured = null;
        var cut = RenderComponent<FileDropZone>(p =>
        {
            p.Add(c => c.AllowedExtensions, new[] { ".md" });
            p.Add(c => c.FilesChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(
                    this, files => captured = files));
        });

        var pdfFile = new FakeBrowserFile("report.pdf", 512);
        var input = cut.FindComponent<InputFile>();
        input.InvokeAsync(() =>
            input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([pdfFile])));

        cut.WaitForState(() => cut.FindAll("[data-testid='file-drop-errors']").Count > 0);

        cut.Find("[data-testid='file-drop-errors']");
        Assert.Contains("unsupported type", cut.Markup);
        Assert.True(captured is null || captured.Count == 0);
    }

    [Fact]
    public void MaxFilesExceeded_ShowsMaxFilesError()
    {
        IReadOnlyList<IBrowserFile>? captured = null;
        var cut = RenderComponent<FileDropZone>(p =>
        {
            p.Add(c => c.MaxFiles, 1);
            p.Add(c => c.AllowedExtensions, new[] { ".md" });
            p.Add(c => c.MaxFileSizeBytes, 10 * 1024 * 1024L);
            p.Add(c => c.FilesChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(
                    this, files => captured = files));
        });

        var f1 = new FakeBrowserFile("a.md", 100);
        var f2 = new FakeBrowserFile("b.md", 100);
        var input = cut.FindComponent<InputFile>();
        input.InvokeAsync(() =>
            input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([f1, f2])));

        cut.WaitForState(() => cut.FindAll("[data-testid='file-drop-errors']").Count > 0);

        Assert.Contains("Maximum", cut.Markup);
        Assert.NotNull(captured);
        Assert.Single(captured!);
    }

    /// <summary>
    /// Minimal <see cref="IBrowserFile"/> stub for unit tests.
    /// </summary>
    private sealed class FakeBrowserFile(string name, long size) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public long Size { get; } = size;
        public string ContentType => "text/plain";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => Stream.Null;
    }
}
