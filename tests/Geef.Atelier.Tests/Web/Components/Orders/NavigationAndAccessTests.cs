using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Web.Components.Layout;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The main navigation leads to "New" and "My work"; everything else sits in the "Workshop" menu, which only administrators
/// see while <see cref="UiOptions.WorkshopForAdminsOnly"/> is on (shipped). The learnings page lists entries of all users and
/// is therefore for administrators only.
/// </summary>
public sealed class NavigationAndAccessTests : TestContext
{
    [Fact]
    public void TheMainNavigationLeadsToNewAndMyWork()
    {
        this.AddTestAuthorization().SetAuthorized("alice");

        var cut = RenderComponent<NavMenu>();

        Assert.Equal("/start", cut.Find("[data-testid='nav-new']").GetAttribute("href"));
        Assert.Equal("/work", cut.Find("[data-testid='nav-my-work']").GetAttribute("href"));
        var visible = cut.FindAll(".nav-links > a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(new[] { "/start", "/work" }, visible);
        Assert.Empty(cut.FindAll("[data-testid='nav-workshop']"));
    }

    [Fact]
    public void TheWorkshopMenuKeepsDashboardRunsAndTheOldForm()
    {
        this.AddTestAuthorization().SetAuthorized("root").SetRoles("admin");
        var cut = RenderComponent<NavMenu>();

        Assert.Equal("Workshop", cut.Find("[data-testid='nav-workshop']").TextContent.Replace("▾", "").Trim());
        cut.Find("[data-testid='nav-workshop']").Click();

        var hrefs = cut.FindAll(".nav-dropdown a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains("/workshop", hrefs);
        Assert.Contains("/runs", hrefs);
        Assert.Contains("/new", hrefs);
        Assert.Contains("/crew/templates", hrefs);
        Assert.Contains("/crew/knowledge", hrefs);
    }

    [Fact]
    public void LearningsAreHiddenFromNormalUsersWhenTheWorkshopIsShownToEveryone()
    {
        Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
        this.AddTestAuthorization().SetAuthorized("alice");
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-workshop']").Click();

        Assert.DoesNotContain("/crew/learnings", cut.FindAll("a.nav-link").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void LearningsAreListedForAdministrators()
    {
        this.AddTestAuthorization().SetAuthorized("root").SetRoles("admin");
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-workshop']").Click();

        Assert.Contains("/crew/learnings", cut.FindAll("a.nav-link").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void TheLearningsPageRequiresTheAdminPolicy()
    {
        var attribute = Assert.Single(typeof(LearningsIndex).GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.Equal("AdminPolicy", attribute.Policy);
    }

    [Theory]
    [InlineData(typeof(Start), "/start")]
    [InlineData(typeof(OrderDetail), "/orders/{orderId:guid}")]
    [InlineData(typeof(MyWork), "/work")]
    public void TheNewPagesNeedASignIn(Type page, string route)
    {
        Assert.Contains(page.GetCustomAttributes<RouteAttribute>(), r => r.Template == route);
        var attribute = Assert.Single(page.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Null(attribute.Policy);
    }

    [Fact]
    public void SignInLeadsToTheStartPage()
    {
        Assert.Equal("/start", Login.ResolvePostLogin(null));
    }
}
