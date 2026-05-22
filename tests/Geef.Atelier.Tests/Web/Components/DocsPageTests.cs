using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Web.Components.Pages.Public;
using Geef.Atelier.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class DocsPageTests : TestContext
{
    private void SetupServices()
    {
        Services.AddSingleton<DocsService>();
        this.AddTestAuthorization().SetAuthorized("stefan");
    }

    // ── Sidebar ──────────────────────────────────────────────────────────

    [Fact]
    public void Docs_Sidebar_ListsAllAllowlistedEntries()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        var navItems = cut.FindAll("[data-testid^='sidebar-']");
        Assert.Equal(DocsService.Entries.Count, navItems.Count);
    }

    [Fact]
    public void Docs_Sidebar_ContainsReadmeEntry()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        cut.Find("[data-testid='sidebar-readme']");
    }

    [Fact]
    public void Docs_Sidebar_ActiveSlugHasActiveClass()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "02-architecture"));

        var activeLink = cut.Find("[data-testid='sidebar-02-architecture']");
        Assert.Contains("active", activeLink.ClassName ?? "");
    }

    [Fact]
    public void Docs_Sidebar_InactiveSlugHasNoActiveClass()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        var otherLink = cut.Find("[data-testid='sidebar-01-vision-and-scope']");
        Assert.DoesNotContain("active", otherLink.ClassName ?? "");
    }

    // ── Unknown slug → not-found ──────────────────────────────────────────

    [Fact]
    public void Docs_UnknownSlug_ShowsNotFoundState()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "does-not-exist"));

        cut.Find("[data-testid='docs-not-found']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='docs-article']"));
    }

    [Fact]
    public void Docs_DisallowedSlug_ShowsNotFoundState()
    {
        SetupServices();

        // 05-decisions-log is intentionally excluded from the allowlist
        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "05-decisions-log"));

        cut.Find("[data-testid='docs-not-found']");
    }

    // ── Known slug → article rendered ─────────────────────────────────────

    [Fact]
    public void Docs_KnownSlug_ShowsArticle()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        cut.Find("[data-testid='docs-article']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='docs-not-found']"));
    }

    // ── Default slug ──────────────────────────────────────────────────────

    [Fact]
    public void Docs_NullSlug_DefaultsToReadme()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, (string?)null));

        // readme should be active
        var readmeLink = cut.Find("[data-testid='sidebar-readme']");
        Assert.Contains("active", readmeLink.ClassName ?? "");
    }

    // ── Language toggle ────────────────────────────────────────────────────

    [Fact]
    public void Docs_LanguageToggle_EnLinkHasNoLangParam()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        // EN link points to /docs/readme without ?lang=de
        var enLink = cut.Find("a.lang-toggle[aria-label='English version']");
        var href = enLink.GetAttribute("href") ?? "";
        Assert.DoesNotContain("lang=de", href);
    }

    [Fact]
    public void Docs_LanguageToggle_DeLinkHasLangParam()
    {
        SetupServices();

        var cut = RenderComponent<Docs>(p => p.Add(c => c.Slug, "readme"));

        var deLink = cut.Find("a.lang-toggle[aria-label='Deutsche Version']");
        var href = deLink.GetAttribute("href") ?? "";
        Assert.Contains("lang=de", href);
    }
}
