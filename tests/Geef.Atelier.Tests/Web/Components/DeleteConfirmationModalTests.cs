using Bunit;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class DeleteConfirmationModalTests : TestContext
{
    [Fact]
    public void ShowFalse_ModalNotRendered()
    {
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, false);
            p.Add(c => c.ItemName, "my-item");
        });

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void ShowTrue_ModalVisible_ConfirmButtonDisabledWhenInputEmpty()
    {
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ItemName, "my-item");
        });

        cut.Find("[data-testid='delete-confirm-input']");
        var btn = cut.Find("[data-testid='delete-confirm-button']");
        Assert.NotNull(btn.GetAttribute("disabled"));
    }

    [Fact]
    public void WrongTextTyped_ConfirmButtonStillDisabled()
    {
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ItemName, "my-item");
        });

        cut.Find("[data-testid='delete-confirm-input']").Input("wrong-name");

        Assert.NotNull(cut.Find("[data-testid='delete-confirm-button']").GetAttribute("disabled"));
    }

    [Fact]
    public void CorrectNameTyped_ConfirmButtonEnabled()
    {
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ItemName, "my-item");
        });

        cut.Find("[data-testid='delete-confirm-input']").Input("my-item");

        Assert.Null(cut.Find("[data-testid='delete-confirm-button']").GetAttribute("disabled"));
    }

    [Fact]
    public void ClickConfirmWithCorrectName_InvokesOnConfirm()
    {
        var confirmed = false;
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ItemName, "my-item");
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true));
        });

        cut.Find("[data-testid='delete-confirm-input']").Input("my-item");
        cut.Find("[data-testid='delete-confirm-button']").Click();

        Assert.True(confirmed);
    }

    [Fact]
    public void ClickCancel_InvokesOnCancel()
    {
        var cancelled = false;
        var cut = RenderComponent<DeleteConfirmationModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.ItemName, "my-item");
            p.Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true));
        });

        // Cancel button is the ghost button (second button)
        var buttons = cut.FindAll("button[type='button']");
        var cancelBtn = buttons.First(b => b.TextContent.Contains("Cancel"));
        cancelBtn.Click();

        Assert.True(cancelled);
    }
}
