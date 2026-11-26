using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI.Landing;
using Geef.Atelier.Web.Display;
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
    public void LandingNav_SignedIn_OffersMyWorkAndANewText()
    {
        this.AddTestAuthorization().SetAuthorized("someone");
        var cut = RenderComponent<LandingNav>();
        Assert.Equal("/work", cut.Find("[data-testid='landing-nav-my-work']").GetAttribute("href"));
        Assert.Equal("/start", cut.Find("[data-testid='landing-nav-start']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a[href='/login']"));
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
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/how-it-works");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/#for-whom");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/#teams");
        Assert.Contains(midLinks, a => a.GetAttribute("href") == "/self-host");
        Assert.DoesNotContain(midLinks, a => a.TextContent.Contains("crew", StringComparison.OrdinalIgnoreCase) || a.TextContent.Contains("Manuscript", StringComparison.Ordinal));
    }

    // ── Hero ─────────────────────────────────────────────────────────────

    private static HowItWorksFeatures Everyone(bool signedIn = false) => new(
        ForSignedInPerson: signedIn, Questions: true, MaxQuestions: 3, ChangeText: true, MaxVersions: 10, WebResearch: false,
        FactCheck: FactCheckOffer.ReportsOnly, SumsInFiles: true, UploadThroughAssistant: true, OlderAssistantTools: true,
        LearnsFromEarlierTexts: false, MaxFiles: 10, MaxFileBytes: 26_214_400, FullTextChars: 150_000, MapChars: 600_000);

    [Fact]
    public void LandingHero_SaysWhatItIsInTheHeadline()
    {
        var cut = RenderComponent<LandingHero>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal("A finished text, and what you still need to check.", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Writes your letters, emails and notices", cut.Find(".colophon-row").TextContent.Trim());
    }

    [Fact]
    public void LandingHero_SignedOut_StartsAtTheSignInPageAndSaysWhoSetsUpAccounts()
    {
        var cut = RenderComponent<LandingHero>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal("/login", cut.Find("[data-testid='landing-hero-start']").GetAttribute("href"));
        Assert.Equal("/how-it-works", cut.Find("[data-testid='landing-hero-how']").GetAttribute("href"));
        Assert.Contains("administrators", cut.Find("[data-testid='landing-hero-account']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void LandingHero_SignedIn_StartsANewText()
    {
        var cut = RenderComponent<LandingHero>(p => p.Add(c => c.Features, Everyone(signedIn: true)));
        Assert.Equal("/start", cut.Find("[data-testid='landing-hero-start']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='landing-hero-account']"));
    }

    [Fact]
    public void LandingHero_RendersHeroPressIllustrationHiddenFromScreenReaders()
    {
        var cut = RenderComponent<LandingHero>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal("true", cut.Find(".hero-visual").GetAttribute("aria-hidden"));
        cut.Find(".press-anim");
    }

    // ── Steps ────────────────────────────────────────────────────────────

    [Fact]
    public void LandingSteps_ShowsFiveStepsWithQuestionsAndFourWithout()
    {
        var with = RenderComponent<LandingSteps>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal(5, with.FindAll(".lp-flow-step").Count);
        Assert.Equal("Five steps from your words to your text.", with.Find("h2").TextContent);

        var without = RenderComponent<LandingSteps>(p => p.Add(c => c.Features, Everyone() with { Questions = false }));
        Assert.Equal(4, without.FindAll(".lp-flow-step").Count);
        Assert.Empty(without.FindAll("[data-step='questions']"));
    }

    [Fact]
    public void LandingSteps_TheOneMoreTryArcSpansFromTheCheckBackToTheWriter()
    {
        var with = RenderComponent<LandingSteps>(p => p.Add(c => c.Features, Everyone()));
        var style = with.Find("[data-testid='landing-flow']").GetAttribute("style");
        Assert.Contains("--steps: 5", style, StringComparison.Ordinal);
        Assert.Contains("--retry-from: 40%", style, StringComparison.Ordinal);
        Assert.Contains("--retry-to: 40%", style, StringComparison.Ordinal);

        var without = RenderComponent<LandingSteps>(p => p.Add(c => c.Features, Everyone() with { Questions = false }));
        var style4 = without.Find("[data-testid='landing-flow']").GetAttribute("style");
        Assert.Contains("--retry-from: 25%", style4, StringComparison.Ordinal);
        Assert.Contains("--retry-to: 50%", style4, StringComparison.Ordinal);
    }

    // ── Capabilities ─────────────────────────────────────────────────────

    [Fact]
    public void LandingCapabilities_RendersSixCardsAndTheLimits()
    {
        var cut = RenderComponent<LandingCapabilities>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal(6, cut.FindAll(".cap").Count);
        Assert.Contains("150,000 characters (roughly 60 pages)", cut.Find("[data-testid='landing-cap-files']").TextContent, StringComparison.Ordinal);
        cut.Find("[data-testid='landing-limits']");
    }

    // ── Workshop ─────────────────────────────────────────────────────────

    [Fact]
    public void LandingWorkshop_HasTeamsAnchorAndNamesNoModel()
    {
        var cut = RenderComponent<LandingWorkshop>();
        Assert.Equal("teams", cut.Find("section").GetAttribute("id"));
        Assert.Contains("administrators", cut.Find("h2").TextContent, StringComparison.Ordinal);
        Assert.Equal(5, cut.FindAll(".team-row").Count);
    }

    [Fact]
    public void LandingWorkshop_FollowsTheSwitchForWhoSeesTheMenu()
    {
        var cut = RenderComponent<LandingWorkshop>(p => p.Add(c => c.WorkshopForAdminsOnly, false));
        Assert.Equal("The workshop, for teams.", cut.Find("h2").TextContent);
        Assert.Contains("shown to everyone who is signed in", cut.Find("[data-testid='landing-teams-visibility']").TextContent, StringComparison.Ordinal);
    }

    // ── Film ─────────────────────────────────────────────────────────────

    [Fact]
    public void LandingFilm_RendersVideoWithMp4SourceAndPoster()
    {
        var cut = RenderComponent<LandingFilm>();
        var video = cut.Find(".lp-film-video");
        Assert.Equal("media/geef-film-en-poster.jpg", video.GetAttribute("poster"));
        var source = cut.Find(".lp-film-video source");
        Assert.Equal("media/geef-film-en.mp4", source.GetAttribute("src"));
        Assert.Equal("video/mp4", source.GetAttribute("type"));
    }

    [Fact]
    public void LandingFilm_HasPlayOverlayButton()
    {
        var cut = RenderComponent<LandingFilm>();
        var play = cut.Find(".lp-film-play");
        Assert.Equal("button", play.GetAttribute("type"));
    }

    [Fact]
    public void LandingFilm_HasEnglishAndGermanLanguageToggle()
    {
        var cut = RenderComponent<LandingFilm>();
        var en = cut.Find(".lp-film-lang[data-lang=en]");
        var de = cut.Find(".lp-film-lang[data-lang=de]");
        Assert.Contains("is-active", en.GetAttribute("class"));
        Assert.DoesNotContain("is-active", de.GetAttribute("class"));
    }

    [Fact]
    public void LandingFilm_VideoCarriesBothLanguageSources()
    {
        var cut = RenderComponent<LandingFilm>();
        var video = cut.Find(".lp-film-video");
        Assert.Equal("media/geef-film-en.mp4", video.GetAttribute("data-src-en"));
        Assert.Equal("media/geef-film-de.mp4", video.GetAttribute("data-src-de"));
        Assert.Equal("media/geef-film-en-poster.jpg", video.GetAttribute("data-poster-en"));
        Assert.Equal("media/geef-film-de-poster.jpg", video.GetAttribute("data-poster-de"));
    }

    [Fact]
    public void LandingFilm_LangButtonsAndStageShareTheFrame()
    {
        // landing.js scopes the language toggle to the .lp-film-frame ancestor of
        // the stage; the buttons must live in that same frame or the toggle is dead.
        var cut = RenderComponent<LandingFilm>();
        var frame = cut.Find(".lp-film-frame");
        Assert.NotNull(frame.QuerySelector(".lp-film-langs .lp-film-lang"));
        Assert.NotNull(frame.QuerySelector(".lp-film-stage .lp-film-video"));
    }

    // ── Closing ──────────────────────────────────────────────────────────

    [Fact]
    public void LandingClosing_CTALinksToLogin()
    {
        var cut = RenderComponent<LandingClosing>(p => p.Add(c => c.Features, Everyone()));
        Assert.Equal("/login", cut.Find("[data-testid='landing-closing-start']").GetAttribute("href"));
    }

    [Fact]
    public void LandingClosing_SignedIn_LinksToStart()
    {
        var cut = RenderComponent<LandingClosing>(p => p.Add(c => c.Features, Everyone(signedIn: true)));
        Assert.Equal("/start", cut.Find("[data-testid='landing-closing-start']").GetAttribute("href"));
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
    public void Landing_Authenticated_StaysReachableWithoutRedirect()
    {
        SetupAuthenticated();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        RenderComponent<Landing>();
        Assert.Equal("http://localhost/", nav.Uri);
    }
}
