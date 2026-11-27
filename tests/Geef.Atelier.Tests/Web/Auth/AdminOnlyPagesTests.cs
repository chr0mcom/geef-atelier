using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Web.Components.Layout;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>
/// Pages whose fields name environment variables (provider keys, tool secrets, MCP auth headers, grounding auth
/// headers) are only for administrators; the navigation hides them from everyone else.
/// </summary>
public sealed class AdminOnlyPagesTests : TestContext
{
    private static readonly Type[] AdminPages =
    [
        typeof(ProvidersIndex),
        typeof(ProviderTypeSelection),
        typeof(CliProviderEditor),
        typeof(ProviderView),
    ];

    public static TheoryData<Type> SecretReferencePages => new(AdminPages);

    [Theory]
    [MemberData(nameof(SecretReferencePages))]
    public void Page_RequiresAdminPolicy(Type page)
    {
        var attributes = page.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.NotEmpty(attributes);
        Assert.All(attributes, a => Assert.Equal("AdminPolicy", a.Policy));
        Assert.NotEmpty(page.GetCustomAttributes<RouteAttribute>());
    }

    [Fact]
    public void AllPagesWithSecretReferenceFields_AreCovered()
    {
        var pagesWithEnvFields = typeof(ProvidersIndex).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ProvidersIndex).Namespace && t.GetCustomAttributes<RouteAttribute>().Any())
            .Where(t => t.Name.EndsWith("Editor", StringComparison.Ordinal)
                        && (t.Name.Contains("Provider", StringComparison.Ordinal) || t.Name.StartsWith("Tool", StringComparison.Ordinal) || t.Name.StartsWith("Mcp", StringComparison.Ordinal)))
            .ToList();
        var covered = AdminPages.ToHashSet();
        Assert.All(pagesWithEnvFields, t => Assert.Contains(t, covered));
    }

    [Fact]
    public void AccessDeniedPage_IsReachableForSignedInUsers()
    {
        var attribute = typeof(AccessDenied).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attribute);
        Assert.Null(attribute.Policy);
        Assert.Equal("/access-denied", typeof(AccessDenied).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void AccessDeniedNotice_SaysSo()
    {
        var cut = RenderComponent<AccessDeniedNotice>();
        Assert.Contains("only open to administrators", cut.Find("[data-testid='access-denied']").TextContent);
    }

    [Fact]
    public void NavMenu_HidesAdminLinks_ForNormalUsers_WhenTheWorkshopIsShownToEveryone()
    {
        Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
        this.AddTestAuthorization().SetAuthorized("alice");
        var cut = RenderComponent<NavMenu>();
        cut.Find("button.nav-group-trigger").Click();

        var hrefs = cut.FindAll("a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains("/runs", hrefs);
        Assert.DoesNotContain("/crew/providers", hrefs);
    }

    [Fact]
    public void NavMenu_ShowsAdminLinks_ForAdmins()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("admin");
        var cut = RenderComponent<NavMenu>();
        cut.Find("button.nav-group-trigger").Click();

        var hrefs = cut.FindAll("a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains("/crew/providers", hrefs);
    }
}
