using Bunit;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ModalTests : TestContext
{
    [Fact]
    public void ShowFalse_RendersNothing()
    {
        var cut = RenderComponent<Modal>(p => p.Add(c => c.Show, false));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void ShowTrue_RendersBackdropAndCard()
    {
        var cut = RenderComponent<Modal>(p => p.Add(c => c.Show, true));

        cut.Find(".modal-backdrop");
        cut.Find(".modal-card");
    }

    [Fact]
    public void CloseOnBackdropClickTrue_BackdropClick_InvokesOnClose()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.CloseOnBackdropClick, true);
            p.Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true));
        });

        cut.Find(".modal-backdrop").Click();

        Assert.True(closed);
    }

    [Fact]
    public void CloseOnBackdropClickFalse_BackdropClick_DoesNotInvokeOnClose()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.CloseOnBackdropClick, false);
            p.Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true));
        });

        cut.Find(".modal-backdrop").Click();

        Assert.False(closed);
    }

    [Fact]
    public void ChildContent_IsRenderedInBody()
    {
        var cut = RenderComponent<Modal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ChildContent, (RenderFragment)(builder =>
            {
                builder.AddContent(0, "Hello from body");
            }));
        });

        Assert.Contains("Hello from body", cut.Find(".modal-body").TextContent);
    }

    [Fact]
    public void DataTestId_IsAppliedToModalCard()
    {
        var cut = RenderComponent<Modal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DataTestId, "my-modal");
        });

        cut.Find("[data-testid='my-modal']");
    }
}
