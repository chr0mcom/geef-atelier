using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class EmailObfuscatedTests : TestContext
{
    [Fact]
    public void EmailObfuscated_SplitsIntoUserAndDomain()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "hello@example.com"));

        var anchor = cut.Find("a.obf-email");
        Assert.Equal("hello", anchor.GetAttribute("data-email-user"));
        Assert.Equal("example.com", anchor.GetAttribute("data-email-domain"));
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_ContainsNoPlaintextMailto()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "secret@domain.org"));

        // The href="#" placeholder must not contain the mailto: URI in static HTML.
        var anchor = cut.Find("a.obf-email");
        var href = anchor.GetAttribute("href") ?? "";
        Assert.DoesNotContain("mailto:", href);
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_DoesNotContainFullEmailAddress()
    {
        const string email = "contact@atelier.test";
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, email));

        // The full assembled address must not appear in the raw HTML served to crawlers.
        Assert.DoesNotContain(email, cut.Markup);
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_ContainsHiddenPlaceholder()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "user@host.de"));

        // Before JS runs, the link shows a placeholder via HTML entities.
        // Both "&#91;" (bracket entity) and the data-attributes confirm obfuscation is present.
        Assert.True(
            cut.Markup.Contains("&#91;") || cut.Markup.Contains("[E-Mail]"),
            $"Expected bracket placeholder in markup: {cut.Markup}");
    }

    [Fact]
    public void EmailObfuscated_ScriptUsesCharCode64_NotLiteralAt()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "x@y.com"));

        // Verify that the inline script uses String.fromCharCode(64) rather than a literal @.
        Assert.Contains("String.fromCharCode(64)", cut.Markup);
        Assert.DoesNotContain("\"@\"", cut.Markup);
        Assert.DoesNotContain("'@'", cut.Markup);
    }

    [Fact]
    public void EmailObfuscated_HasAriaLabel()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "a@b.com"));

        var anchor = cut.Find("a.obf-email");
        var label = anchor.GetAttribute("aria-label") ?? "";
        Assert.False(string.IsNullOrWhiteSpace(label));
    }
}
