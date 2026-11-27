using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.Pages.Public;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The page "Host it yourself", linked from the navigation and the footer of the start page, says the same as the start page about
/// where words and files go: kept on the own server, sent to the AI services that are connected. It uses plain words.
/// </summary>
public sealed class SelfHostWordsTests : TestContext
{
    private static readonly string[] Forbidden =
    [
        "crew", "run", "briefing", "grounding", "pipeline", "iteration", "convergence", "finalizer", "manuscript", "workshop", "GEEF loop",
        "never leave", "your own iron", "no vendor lock-in", "Fifteen tools",
    ];

    [Fact]
    public void It_says_what_stays_on_the_server_and_what_goes_to_the_connected_AI_services()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<SelfHost>();

        Assert.Contains("On your server:", cut.Find("[data-testid='self-host-stays']").TextContent, StringComparison.Ordinal);
        Assert.Contains("to the AI services you connect", cut.Find("[data-testid='self-host-leaves']").TextContent, StringComparison.Ordinal);
        Assert.Contains("to the AI services you connect", SelfHost.Description, StringComparison.Ordinal);
        var search = cut.Find("[data-testid='self-host-search']").TextContent;
        Assert.Contains("without one, nothing goes to a web search", search, StringComparison.Ordinal);
        Assert.Contains("never files or answers", search, StringComparison.Ordinal);
        Assert.Contains("names or other details from the request can be among them", search, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void It_uses_none_of_the_old_words_or_promises()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var text = RenderComponent<SelfHost>().Find("[data-testid='self-host']").TextContent;

        foreach (var word in Forbidden)
            Assert.DoesNotMatch(new Regex($@"\b{Regex.Escape(word)}s?\b", RegexOptions.IgnoreCase), text + " " + SelfHost.Description + " " + SelfHost.Title);
    }

    [Fact]
    public void Every_link_on_the_page_resolves()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<SelfHost>();
        var routes = typeof(Landing).Assembly.GetTypes().SelectMany(t => t.GetCustomAttributes<RouteAttribute>()).Select(r => r.Template).ToHashSet();
        var landingIds = RenderComponent<Landing>().FindAll("[id]").Select(e => e.Id!).ToHashSet();

        foreach (var href in cut.FindAll("a[href]").Select(a => a.GetAttribute("href")!).Distinct())
        {
            if (href.StartsWith("https://github.com/chr0mcom/", StringComparison.Ordinal))
                continue;
            if (href.StartsWith("/#", StringComparison.Ordinal))
                Assert.Contains(href[2..], landingIds);
            else if (href.StartsWith("/docs/", StringComparison.Ordinal))
                Assert.True(DocsService.IsAllowed(href["/docs/".Length..]), href);
            else
                Assert.True(routes.Contains(href), $"No page for {href}");
        }
    }
}
