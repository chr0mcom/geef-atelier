using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Components;
using HowItWorksPage = Geef.Atelier.Web.Components.Pages.HowItWorks;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// Every link on the start page leads somewhere: a page of this application, a document it serves, an anchor that exists on the
/// start page or on "How it works", or the public source code. No link names a domain of an installation.
/// </summary>
public sealed class LandingLinksTests : TestContext
{
    private static readonly string[] AllowedOutside = ["https://github.com/chr0mcom/geef-atelier", "https://github.com/chr0mcom/geef"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_link_on_the_start_page_resolves(bool signedIn)
    {
        var auth = this.AddTestAuthorization();
        if (signedIn)
            auth.SetAuthorized("tester");
        else
            auth.SetNotAuthorized();

        var landing = RenderComponent<Landing>();
        var landingIds = landing.FindAll("[id]").Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
        var explanationIds = RenderComponent<HowItWorksPage>().FindAll("[id]").Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
        var routes = typeof(Landing).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var links = landing.FindAll("a[href]").Select(a => a.GetAttribute("href")!).Distinct().ToList();
        Assert.NotEmpty(links);
        foreach (var href in links)
        {
            if (href.StartsWith("https://", StringComparison.Ordinal))
            {
                Assert.True(AllowedOutside.Any(o => href == o || href.StartsWith(o + "/", StringComparison.Ordinal)), $"Outside link not allowed: {href}");
                continue;
            }
            Assert.StartsWith("/", href, StringComparison.Ordinal);
            var hash = href.IndexOf('#', StringComparison.Ordinal);
            var path = hash < 0 ? href : href[..hash];
            var anchor = hash < 0 ? null : href[(hash + 1)..];

            if (path.StartsWith("/docs/", StringComparison.Ordinal))
                Assert.True(DocsService.IsAllowed(path["/docs/".Length..]), $"Document not served: {href}");
            else
                Assert.True(routes.Contains(path), $"No page for {href}");

            if (anchor is null)
                continue;
            var ids = path switch
            {
                "/" => landingIds,
                "/how-it-works" => explanationIds,
                _ => throw new Xunit.Sdk.XunitException($"Anchor on a page this test does not render: {href}"),
            };
            Assert.True(ids.Contains(anchor), $"Anchor #{anchor} does not exist on {path} ({href})");
        }
    }

    [Fact]
    public void The_navigation_and_footer_of_the_public_pages_link_only_to_existing_anchors_of_the_start_page()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var landingIds = RenderComponent<Landing>().FindAll("[id]").Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
        var shared = RenderComponent<Geef.Atelier.Web.Components.UI.Landing.LandingNav>().FindAll("a[href^='/#']")
            .Concat(RenderComponent<Geef.Atelier.Web.Components.UI.Landing.LandingFooter>().FindAll("a[href^='/#']"))
            .Select(a => a.GetAttribute("href")![2..]);

        Assert.All(shared, id => Assert.Contains(id, landingIds));
    }

    /// <summary>The root of the repository, found from the test binaries.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
