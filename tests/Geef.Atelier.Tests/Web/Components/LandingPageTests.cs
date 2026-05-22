using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI.Landing;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class LandingPageTests : TestContext
{
    private void SetupAnonymous()
    {
        this.AddTestAuthorization().SetNotAuthorized();
    }

    private void SetupAuthenticated(string username = "stefan")
    {
        this.AddTestAuthorization().SetAuthorized(username);
    }

    // ── Nav ──────────────────────────────────────────────────────────────

    [Fact]
    public void LandingNav_RendersSignInLinkToLogin()
    {
        var cut = RenderComponent<LandingNav>();
        var link = cut.Find("a.signin");
        Assert.Equal("/login", link.GetAttribute("href"));
    }

    [Fact]
    public void LandingNav_RendersCTALinkToLogin()
    {
        var cut = RenderComponent<LandingNav>();
        var cta = cut.Find("a.lp-cta");
        Assert.Equal("/login", cta.GetAttribute("href"));
    }

    [Fact]
    public void LandingNav_MidNavLinksPointToAnchors()
    {
        var cut = RenderComponent<LandingNav>();
        var midLinks = cut.FindAll(".midnav a");
        // Absolute (/#…) so the links also resolve from the public stub pages,
        // where the nav is shared but the anchor targets live on the landing page.
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/#geef");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/#crew");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/#proof");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/self-host");
    }

    // ── Hero ─────────────────────────────────────────────────────────────

    [Fact]
    public void LandingHero_ContainsManufactoryHeadline()
    {
        var cut = RenderComponent<LandingHero>();
        Assert.Contains("manufactory", cut.Find("h1").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LandingHero_CTAsLinkToLogin()
    {
        var cut = RenderComponent<LandingHero>();
        var ctas = cut.FindAll("a.lp-cta");
        Assert.All(ctas, a =>
        {
            var href = a.GetAttribute("href") ?? "";
            Assert.True(href == "/login" || href == "#geef", $"Unexpected href: {href}");
        });
    }

    [Fact]
    public void LandingHero_RendersHeroPressIllustration()
    {
        var cut = RenderComponent<LandingHero>();
        cut.Find(".press-anim");
    }

    // ── GeefFlow ─────────────────────────────────────────────────────────

    [Fact]
    public void LandingGeefFlow_RendersFourPhases()
    {
        var cut = RenderComponent<LandingGeefFlow>();
        var phases = cut.FindAll(".geef-phase");
        Assert.Equal(4, phases.Count);
    }

    [Fact]
    public void LandingGeefFlow_HasGeefAnchorId()
    {
        var cut = RenderComponent<LandingGeefFlow>();
        var section = cut.Find("section");
        Assert.Equal("geef", section.GetAttribute("id"));
    }

    [Fact]
    public void LandingGeefFlow_EvaluationPhaseHasIterBadge()
    {
        var cut = RenderComponent<LandingGeefFlow>();
        var badge = cut.Find(".iter-badge");
        Assert.Contains("×3", badge.TextContent);
    }

    [Fact]
    public void LandingGeefFlow_ContainsIterationLoop()
    {
        var cut = RenderComponent<LandingGeefFlow>();
        cut.Find(".loop");
        cut.Find(".loop-label");
    }

    [Fact]
    public void LandingGeefFlow_StartsWithRunClassPresent_ForVisibility()
    {
        var cut = RenderComponent<LandingGeefFlow>();
        // Section starts with "in" for immediate visibility (animation driven by JS)
        var section = cut.Find("section");
        Assert.Contains("in", section.ClassList);
    }

    // ── Crew ─────────────────────────────────────────────────────────────

    [Fact]
    public void LandingCrew_HasCrewAnchorId()
    {
        var cut = RenderComponent<LandingCrew>();
        var section = cut.Find("section");
        Assert.Equal("crew", section.GetAttribute("id"));
    }

    [Fact]
    public void LandingCrew_RendersCrewSheet()
    {
        var cut = RenderComponent<LandingCrew>();
        cut.Find(".crew-sheet");
    }

    // ── Proof ─────────────────────────────────────────────────────────────

    [Fact]
    public void LandingProof_HasProofAnchorId()
    {
        var cut = RenderComponent<LandingProof>();
        var section = cut.Find("section");
        Assert.Equal("proof", section.GetAttribute("id"));
    }

    [Fact]
    public void LandingProof_RendersRunMock()
    {
        var cut = RenderComponent<LandingProof>();
        cut.Find(".run-mock");
    }

    // ── Capabilities ─────────────────────────────────────────────────────

    [Fact]
    public void LandingCapabilities_RendersSixCapCards()
    {
        var cut = RenderComponent<LandingCapabilities>();
        var cards = cut.FindAll(".cap");
        Assert.Equal(6, cards.Count);
    }

    // ── Closing ──────────────────────────────────────────────────────────

    [Fact]
    public void LandingClosing_CTALinksToLogin()
    {
        var cut = RenderComponent<LandingClosing>();
        var cta = cut.Find("a.lp-cta");
        Assert.Equal("/login", cta.GetAttribute("href"));
    }

    // ── Landing page (auth routing) ───────────────────────────────────────

    [Fact]
    public void Landing_Anonymous_RendersPageContent()
    {
        SetupAnonymous();
        var cut = RenderComponent<Landing>();
        cut.Find(".landing");
    }

    [Fact]
    public void Landing_Anonymous_DoesNotRedirect()
    {
        SetupAnonymous();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        RenderComponent<Landing>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void Landing_Authenticated_RedirectsToWorkshop()
    {
        SetupAuthenticated();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        RenderComponent<Landing>();
        Assert.EndsWith("/workshop", nav.Uri);
    }
}
