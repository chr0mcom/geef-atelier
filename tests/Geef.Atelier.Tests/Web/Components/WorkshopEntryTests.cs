using System.Reflection;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Web.Components.Layout;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.Pages.Account;
using Geef.Atelier.Web.Components.Pages.Admin;
using Geef.Atelier.Web.Display;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// Stage 8a (D-077): the entries to the workshop pages are shown only to administrators while
/// <see cref="UiOptions.WorkshopForAdminsOnly"/> is on (shipped); the switch off through the environment brings them back for
/// everyone. Only the entries change: pages, routes and access rights stay as they were, and the account pages lead back to
/// "My work" instead of into the hidden workshop.
/// </summary>
public sealed class WorkshopEntryTests : TestContext
{
    private static readonly Type[] WorkshopPagesOpenToEveryone =
    [
        typeof(Runs),
        typeof(RunDetail),
    ];

    public static TheoryData<Type> OpenWorkshopPages => new(WorkshopPagesOpenToEveryone);

    [Fact]
    public void The_switch_ships_on_and_defaults_to_on()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        Assert.Equal("True", shipped[$"{UiOptions.SectionName}:{nameof(UiOptions.WorkshopForAdminsOnly)}"], ignoreCase: true);
        Assert.True(shipped.GetSection(UiOptions.SectionName).Get<UiOptions>()!.WorkshopForAdminsOnly);
        Assert.True(new UiOptions().WorkshopForAdminsOnly);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void Only_administrators_see_the_entries_while_the_switch_is_on(bool adminsOnly, bool admin, bool expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "alice") };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, OwnerScope.AdminRole));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));

        Assert.Equal(expected, new UiOptions { WorkshopForAdminsOnly = adminsOnly }.ShowsWorkshopEntriesTo(user));
    }

    [Fact]
    public void Nobody_signed_out_sees_the_entries_while_the_switch_is_on()
    {
        Assert.False(new UiOptions().ShowsWorkshopEntriesTo(null));
        Assert.False(new UiOptions().ShowsWorkshopEntriesTo(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void A_normal_user_sees_only_new_my_work_and_how_it_works_in_the_menu()
    {
        this.AddTestAuthorization().SetAuthorized("alice");

        var cut = RenderComponent<NavMenu>();

        Assert.Equal(new[] { "/start", "/work", "/how-it-works" }, cut.FindAll(".nav-links a.nav-link").Select(a => a.GetAttribute("href")).ToArray());
        Assert.Empty(cut.FindAll("[data-testid='nav-workshop']"));
        Assert.DoesNotContain("Workshop", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_administrator_keeps_the_whole_workshop_menu()
    {
        this.AddTestAuthorization().SetAuthorized("root").SetRoles(OwnerScope.AdminRole);

        var cut = RenderComponent<NavMenu>();
        cut.Find("[data-testid='nav-workshop']").Click();

        var hrefs = cut.FindAll(".nav-dropdown a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(
            new[] { "/runs", "/crew/providers" },
            hrefs);
    }

    [Fact]
    public void With_the_switch_off_a_normal_user_gets_the_menu_back_as_before()
    {
        Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
        this.AddTestAuthorization().SetAuthorized("alice");

        var cut = RenderComponent<NavMenu>();
        cut.Find("[data-testid='nav-workshop']").Click();

        var hrefs = cut.FindAll(".nav-dropdown a.nav-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(
            new[] { "/runs" },
            hrefs);
    }

    [Theory]
    [InlineData(typeof(ConnectedClients))]
    [InlineData(typeof(OAuthClients))]
    public void The_account_pages_lead_a_normal_user_back_to_my_work(Type page)
    {
        AddPageServices();
        this.AddTestAuthorization().SetAuthorized("alice");

        var cut = RenderPage(page);

        Assert.Equal(WorkshopEntryExtensions.LayBackLink, cut.Find("[data-testid='btn-back']").GetAttribute("href"));
        Assert.Single(cut.FindAll("[data-testid='btn-back']"));
    }

    [Theory]
    [InlineData(typeof(ConnectedClients), "/runs", true, false)]
    [InlineData(typeof(ConnectedClients), "/runs", false, true)]
    [InlineData(typeof(OAuthClients), "/runs", true, false)]
    [InlineData(typeof(OAuthClients), "/runs", false, true)]
    public void The_account_pages_keep_their_old_way_back_for_administrators_and_with_the_switch_off(Type page, string back, bool admin, bool switchOff)
    {
        AddPageServices();
        if (switchOff)
            Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(admin ? "root" : "alice");
        if (admin)
            auth.SetRoles(OwnerScope.AdminRole);

        var cut = RenderPage(page);

        Assert.Equal(back, cut.Find("[data-testid='btn-back']").GetAttribute("href"));
    }

    [Theory]
    [MemberData(nameof(OpenWorkshopPages))]
    public void The_workshop_pages_keep_their_routes_and_stay_open_to_every_signed_in_user(Type page)
    {
        Assert.NotEmpty(page.GetCustomAttributes<RouteAttribute>());
        var attributes = page.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.NotEmpty(attributes);
        Assert.All(attributes, a =>
        {
            Assert.Null(a.Policy);
            Assert.Null(a.Roles);
        });
    }

    private IRenderedFragment RenderPage(Type page) =>
        Render(builder =>
        {
            builder.OpenComponent(0, page);
            builder.CloseComponent();
        });

    private void AddPageServices()
    {
        Services.AddSingleton(FailingOAuthService.Create());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>An OAuth service whose every call fails; the account pages show their error and still render the way back.</summary>
    public class FailingOAuthService : DispatchProxy
    {
        public static IOAuthService Create() => Create<IOAuthService, FailingOAuthService>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Not available in this test.");
    }
}
