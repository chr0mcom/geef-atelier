using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The landing page is the first thing a layperson sees: it uses plain words, says that the team loop belongs to the workshop and
/// not to every text, limits "what was checked" to the standard way and leads to the explanation page with a full page load.
/// </summary>
public sealed class LandingWordsTests : TestContext
{
    [Fact]
    public void The_landing_page_uses_none_of_the_technical_words_of_the_older_way()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var text = RenderComponent<Landing>().Find(".landing").TextContent;

        foreach (var word in new[] { "crew", "manuscript", "iteration", "iter", "convergence", "converged", "converges", "briefing", "executor", "finalizer", "run" })
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), text);
        Assert.DoesNotContain("powers every run", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("learns", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Every text written the standard way comes with what was checked", text, StringComparison.Ordinal);
        Assert.Contains("The standard way for everyone is simpler", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_links_to_the_explanation_page_load_it_as_a_full_page()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var links = RenderComponent<Landing>().FindAll("a[href='/how-it-works']");

        Assert.True(links.Count >= 3);
        Assert.All(links, a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
    }

    [Fact]
    public void Signed_out_the_menu_offers_to_sign_in()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor>(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<UserMenu>();

        Assert.Equal("/login", cut.Find("[data-testid='menu-sign-in']").GetAttribute("href"));
    }
}
