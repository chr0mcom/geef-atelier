using Bunit;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;
using static Geef.Atelier.Web.Components.UI.PromoteAttachmentModal;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class PromoteAttachmentModalTests : TestContext
{
    private static KnowledgeDocument MakeDoc(string title = "Test Document") =>
        new(
            Id:                  Guid.NewGuid(),
            Title:               title,
            Description:         "A description.",
            OriginalFilename:    "doc.md",
            ContentType:         "text/markdown",
            FileSizeBytes:       1024,
            RawContent:          "Content.",
            Tags:                [],
            EmbeddingModel:      "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount:          1,
            IndexingCostEur:     null,
            CreatedAt:           DateTimeOffset.UtcNow,
            UpdatedAt:           DateTimeOffset.UtcNow,
            Scope:               KnowledgeScope.RunLocal,
            RunId:               Guid.NewGuid());

    [Fact]
    public void ShowFalse_RendersNothing()
    {
        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, false);
            p.Add(c => c.Document, MakeDoc());
        });

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void ShowTrue_RendersModal()
    {
        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.Document, MakeDoc());
        });

        cut.Find("[data-testid='promote-modal']");
    }

    [Fact]
    public void ShowTrue_RendersConfirmButton()
    {
        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.Document, MakeDoc());
        });

        cut.Find("[data-testid='promote-confirm-button']");
    }

    [Fact]
    public void ClickConfirm_InvokesOnConfirmWithDocumentId()
    {
        PromoteRequest? captured = null;
        var doc = MakeDoc("My Doc");

        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.Document, doc);
            p.Add(c => c.OnConfirm,
                EventCallback.Factory.Create<PromoteRequest>(this, r => captured = r));
        });

        cut.Find("[data-testid='promote-confirm-button']").Click();

        Assert.NotNull(captured);
        Assert.Equal(doc.Id, captured!.DocumentId);
    }

    [Fact]
    public void TitleOverride_PassedInRequest()
    {
        PromoteRequest? captured = null;
        var doc = MakeDoc();

        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.Document, doc);
            p.Add(c => c.OnConfirm,
                EventCallback.Factory.Create<PromoteRequest>(this, r => captured = r));
        });

        cut.Find("[data-testid='promote-title-input']").Change("New Title");
        cut.Find("[data-testid='promote-confirm-button']").Click();

        Assert.NotNull(captured);
        Assert.Equal("New Title", captured!.NewTitle);
    }

    [Fact]
    public void BlankTitle_PassedAsNullInRequest()
    {
        PromoteRequest? captured = null;
        var doc = MakeDoc();

        var cut = RenderComponent<PromoteAttachmentModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.Document, doc);
            p.Add(c => c.OnConfirm,
                EventCallback.Factory.Create<PromoteRequest>(this, r => captured = r));
        });

        cut.Find("[data-testid='promote-confirm-button']").Click();

        Assert.NotNull(captured);
        Assert.Null(captured!.NewTitle);
    }
}
