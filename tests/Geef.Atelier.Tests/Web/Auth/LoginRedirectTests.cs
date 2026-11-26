using Geef.Atelier.Web.Components.Pages;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>After signing in, the person only ever lands on a page of this site.</summary>
public sealed class LoginRedirectTests
{
    [Theory]
    [InlineData("/start")]
    [InlineData("/orders/0f8fad5b?change=1")]
    [InlineData("/oauth/consent?client_id=x&redirect_uri=https%3A%2F%2Fexample.org%2Fcb")]
    [InlineData("/")]
    public void A_path_on_this_site_is_kept(string url) => Assert.Equal(url, Login.ResolvePostLogin(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.org/")]
    [InlineData("http://example.org/start")]
    [InlineData("//example.org/start")]
    [InlineData("/\\example.org")]
    [InlineData("\\\\example.org")]
    [InlineData("javascript:alert(1)")]
    [InlineData("start")]
    [InlineData(" /start")]
    [InlineData("/\t/example.org")]
    [InlineData("/\r\n/example.org")]
    public void Anything_else_opens_the_start_page(string? url) => Assert.Equal("/start", Login.ResolvePostLogin(url));
}
