using Bunit;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class RunAttachmentsListTests : TestContext
{
    private static KnowledgeDocument MakeAttachment(string title = "Attachment Doc") =>
        new(
            Id:                  Guid.NewGuid(),
            Title:               title,
            Description:         "",
            OriginalFilename:    "doc.md",
            ContentType:         "text/markdown",
            FileSizeBytes:       2048,
            RawContent:          "Some content.",
            Tags:                [],
            EmbeddingModel:      "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount:          2,
            IndexingCostEur:     null,
            CreatedAt:           DateTimeOffset.UtcNow,
            UpdatedAt:           DateTimeOffset.UtcNow,
            Scope:               KnowledgeScope.RunLocal,
            RunId:               Guid.NewGuid());

    [Fact]
    public void WithAttachments_RendersRunAttachmentsTestId()
    {
        var attachments = new[] { MakeAttachment("Report A") };

        var cut = RenderComponent<RunAttachmentsList>(p =>
            p.Add(c => c.Attachments, attachments));

        cut.Find("[data-testid='run-attachments']");
    }

    [Fact]
    public void WithAttachments_RendersTitle()
    {
        var attachments = new[] { MakeAttachment("Report A") };

        var cut = RenderComponent<RunAttachmentsList>(p =>
            p.Add(c => c.Attachments, attachments));

        Assert.Contains("Report A", cut.Markup);
    }

    [Fact]
    public void WithMultipleAttachments_AllRendered()
    {
        var attachments = new[]
        {
            MakeAttachment("Alpha"),
            MakeAttachment("Beta"),
        };

        var cut = RenderComponent<RunAttachmentsList>(p =>
            p.Add(c => c.Attachments, attachments));

        Assert.Contains("Alpha", cut.Markup);
        Assert.Contains("Beta", cut.Markup);
        Assert.Equal(2, cut.FindAll("[data-testid='run-attachment-item']").Count);
    }

    [Fact]
    public void PromoteButton_Exists_ForEachItem()
    {
        var attachments = new[] { MakeAttachment("Doc 1"), MakeAttachment("Doc 2") };

        var cut = RenderComponent<RunAttachmentsList>(p =>
            p.Add(c => c.Attachments, attachments));

        Assert.Equal(2, cut.FindAll("[data-testid='promote-button']").Count);
    }

    [Fact]
    public void PromoteButton_Click_InvokesOnPromoteClick()
    {
        KnowledgeDocument? promoted = null;
        var doc = MakeAttachment("My Attachment");

        var cut = RenderComponent<RunAttachmentsList>(p =>
        {
            p.Add(c => c.Attachments, new[] { doc });
            p.Add(c => c.OnPromoteClick,
                EventCallback.Factory.Create<KnowledgeDocument>(this, d => promoted = d));
        });

        cut.Find("[data-testid='promote-button']").Click();

        Assert.NotNull(promoted);
        Assert.Equal(doc.Id, promoted!.Id);
    }

    [Fact]
    public void ShowsAttachmentCount()
    {
        var attachments = new[] { MakeAttachment("A"), MakeAttachment("B") };

        var cut = RenderComponent<RunAttachmentsList>(p =>
            p.Add(c => c.Attachments, attachments));

        Assert.Contains("2", cut.Markup);
    }
}
