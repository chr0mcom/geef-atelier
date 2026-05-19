using Bunit;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ResumeRunDialogTests : TestContext
{
    [Fact]
    public void Show_False_DialogNotRendered()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, false);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Show_True_DialogVisible()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        cut.Find("[data-testid='resume-dialog']");
    }

    [Fact]
    public void Show_True_SeedModeSelectedByDefault_WhenHasIterations()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        var seedRadio = cut.Find("[data-testid='resume-mode-seed']");
        Assert.True(seedRadio.HasAttribute("checked"));
    }

    [Fact]
    public void Show_True_CleanModeSelectedByDefault_WhenNoIterations()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, false);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        var cleanRadio = cut.Find("[data-testid='resume-mode-clean']");
        Assert.True(cleanRadio.HasAttribute("checked"));
    }

    [Fact]
    public void MaxIterations_PrefilledWithDefaultValue()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 7);
        });

        var input = cut.Find("[data-testid='resume-max-iterations']");
        Assert.Equal("7", input.GetAttribute("value"));
    }

    [Fact]
    public async Task Confirm_SeedMode_InvokesOnConfirmWithUseSeedDraftTrue()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.True(captured!.UseSeedDraft);
    }

    [Fact]
    public async Task Confirm_CleanMode_InvokesOnConfirmWithUseSeedDraftFalse()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        // Switch to clean mode
        cut.Find("[data-testid='resume-mode-clean']").Change(true);
        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.False(captured!.UseSeedDraft);
    }

    [Fact]
    public async Task Confirm_SendsMaxIterationsOverride()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        cut.Find("[data-testid='resume-max-iterations']").Change("10");
        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.Equal(10, captured!.MaxIterationsOverride);
    }

    [Fact]
    public async Task Confirm_ZeroMaxIterations_SendsNullOverride()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        cut.Find("[data-testid='resume-max-iterations']").Change("0");
        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.Null(captured!.MaxIterationsOverride);
    }

    [Fact]
    public async Task Cancel_InvokesOnCancel()
    {
        var cancelled = false;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true));
        });

        await cut.Find("[data-testid='resume-cancel-button']").ClickAsync(new());

        Assert.True(cancelled);
    }
}
