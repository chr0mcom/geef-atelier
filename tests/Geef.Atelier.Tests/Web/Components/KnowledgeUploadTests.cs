using Bunit;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class KnowledgeUploadTests : TestContext
{
    [Fact]
    public void Form_RendersWithTitleInput()
    {
        (string, string, IReadOnlyList<string>, IBrowserFile?)? submitted = null;
        var cut = RenderComponent<KnowledgeUploadForm>(p =>
            p.Add(c => c.OnSubmit,
                EventCallback.Factory.Create<(string, string, IReadOnlyList<string>, IBrowserFile?)>(
                    this, args => submitted = args)));

        cut.Find("[data-testid='input-title']");
    }

    [Fact]
    public void Form_RendersUploadButton()
    {
        var cut = RenderComponent<KnowledgeUploadForm>(p =>
            p.Add(c => c.OnSubmit,
                EventCallback.Factory.Create<(string, string, IReadOnlyList<string>, IBrowserFile?)>(
                    this, _ => { })));

        var btn = cut.Find("[data-testid='btn-upload']");
        Assert.Contains("Upload", btn.TextContent);
    }

    [Fact]
    public void Submit_WithoutFile_ShowsError()
    {
        var cut = RenderComponent<KnowledgeUploadForm>(p =>
            p.Add(c => c.OnSubmit,
                EventCallback.Factory.Create<(string, string, IReadOnlyList<string>, IBrowserFile?)>(
                    this, _ => { })));

        // Fill in required title field
        cut.Find("[data-testid='input-title']").Change("My Document");

        // Submit without a file
        cut.Find("form").Submit();

        cut.Find("[data-testid='upload-error']");
        Assert.Contains("Please select a file", cut.Markup);
    }

    [Fact]
    public void Form_RendersDescriptionInput()
    {
        var cut = RenderComponent<KnowledgeUploadForm>(p =>
            p.Add(c => c.OnSubmit,
                EventCallback.Factory.Create<(string, string, IReadOnlyList<string>, IBrowserFile?)>(
                    this, _ => { })));

        cut.Find("[data-testid='input-description']");
    }
}
