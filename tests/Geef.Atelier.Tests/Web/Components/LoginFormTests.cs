using Bunit;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class LoginFormTests : TestContext
{
    public LoginFormTests()
    {
        // AntiforgeryToken component requires IAntiforgery to be available.
        Services.AddAntiforgery();
    }

    [Fact]
    public void RendersFormWithPostMethod()
    {
        var cut = RenderComponent<LoginForm>();

        var form = cut.Find("form");
        Assert.Equal("post", form.GetAttribute("method"));
    }

    [Fact]
    public void RendersUsernameAndPasswordInputs()
    {
        var cut = RenderComponent<LoginForm>();

        cut.Find("input#username");
        cut.Find("input#password");
    }

    [Fact]
    public void ShowsErrorBannerWhenErrorParameterSet()
    {
        var cut = RenderComponent<LoginForm>(p => p.Add(c => c.Error, "Ungültige Anmeldedaten"));

        var errorDiv = cut.Find(".login-error");
        Assert.Contains("Ungültige Anmeldedaten", errorDiv.TextContent);
    }

    [Fact]
    public void HidesErrorBannerWhenErrorParameterIsNull()
    {
        var cut = RenderComponent<LoginForm>(p => p.Add(c => c.Error, (string?)null));

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".login-error"));
    }

    [Fact]
    public void RendersHiddenReturnUrlInputWhenSet()
    {
        var cut = RenderComponent<LoginForm>(p => p.Add(c => c.ReturnUrl, "/runs"));

        var hiddenInput = cut.Find("input[type='hidden'][name='ReturnUrl']");
        Assert.Equal("/runs", hiddenInput.GetAttribute("value"));
    }

    [Fact]
    public void OmitsReturnUrlInputWhenNotSet()
    {
        var cut = RenderComponent<LoginForm>(p => p.Add(c => c.ReturnUrl, (string?)null));

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("input[name='ReturnUrl']"));
    }
}
