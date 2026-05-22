using System.Reflection;
using Bunit;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.Pages.Public;
using IndexPage = Geef.Atelier.Web.Components.Pages.Index;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class LandingRoutingTests : TestContext
{
    // ── Dashboard moved to /workshop ──────────────────────────────────────

    [Fact]
    public void Index_HasWorkshopRoute()
    {
        var attrs = typeof(IndexPage).GetCustomAttributes<RouteAttribute>();
        Assert.Contains(attrs, r => r.Template == "/workshop");
    }

    [Fact]
    public void Index_StillRequiresAuthorization()
    {
        Assert.NotNull(typeof(IndexPage).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Index_DoesNotHaveRootRoute()
    {
        var attrs = typeof(IndexPage).GetCustomAttributes<RouteAttribute>();
        Assert.DoesNotContain(attrs, r => r.Template == "/");
    }

    // ── Landing has root route and AllowAnonymous ────────────────────────

    [Fact]
    public void Landing_HasRootRoute()
    {
        var attrs = typeof(Landing).GetCustomAttributes<RouteAttribute>();
        Assert.Contains(attrs, r => r.Template == "/");
    }

    [Fact]
    public void Landing_IsAllowAnonymous()
    {
        Assert.NotNull(typeof(Landing).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ── Login post-login redirect helper ────────────────────────────────

    [Fact]
    public void Login_ResolvePostLogin_NullReturnsWorkshop()
    {
        Assert.Equal("/workshop", Login.ResolvePostLogin(null));
    }

    [Fact]
    public void Login_ResolvePostLogin_EmptyReturnsWorkshop()
    {
        Assert.Equal("/workshop", Login.ResolvePostLogin(""));
    }

    [Fact]
    public void Login_ResolvePostLogin_PreservesReturnUrl()
    {
        Assert.Equal("/runs", Login.ResolvePostLogin("/runs"));
    }

    // ── Stub pages ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(Pricing), "/pricing")]
    [InlineData(typeof(Docs), "/docs")]
    [InlineData(typeof(SelfHost), "/self-host")]
    [InlineData(typeof(Status), "/status")]
    [InlineData(typeof(Changelog), "/changelog")]
    [InlineData(typeof(Contact), "/contact")]
    [InlineData(typeof(Imprint), "/imprint")]
    [InlineData(typeof(Privacy), "/privacy")]
    [InlineData(typeof(Terms), "/terms")]
    public void StubPage_HasCorrectRoute(Type pageType, string expectedRoute)
    {
        var attrs = pageType.GetCustomAttributes<RouteAttribute>();
        Assert.Contains(attrs, r => r.Template == expectedRoute);
    }

    [Theory]
    [InlineData(typeof(Pricing))]
    [InlineData(typeof(Docs))]
    [InlineData(typeof(SelfHost))]
    [InlineData(typeof(Status))]
    [InlineData(typeof(Changelog))]
    [InlineData(typeof(Contact))]
    [InlineData(typeof(Imprint))]
    [InlineData(typeof(Privacy))]
    [InlineData(typeof(Terms))]
    public void StubPage_IsAllowAnonymous(Type pageType)
    {
        Assert.NotNull(pageType.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ── ComingSoon component ─────────────────────────────────────────────

    [Fact]
    public void ComingSoon_RendersTitleAndComingSoonText()
    {
        var cut = RenderComponent<ComingSoon>(p => p.Add(c => c.Title, "Pricing"));
        Assert.Contains("Pricing", cut.Find("h1").TextContent);
        Assert.Contains("Coming soon", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComingSoon_BackLinkPointsToRoot()
    {
        var cut = RenderComponent<ComingSoon>(p => p.Add(c => c.Title, "Test"));
        var link = cut.Find("a[href='/']");
        Assert.NotNull(link);
    }
}
