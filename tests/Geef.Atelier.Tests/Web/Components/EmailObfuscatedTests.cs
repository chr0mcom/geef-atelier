using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class EmailObfuscatedTests : TestContext
{
    public EmailObfuscatedTests()
    {
        // The component calls JS.InvokeVoidAsync("deobfuscateEmail", _ref) in OnAfterRenderAsync;
        // loose mode lets it render in bUnit without an explicit JS setup.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmailObfuscated_SplitsIntoUserAndDomain()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "hello@example.com"));

        var span = cut.Find("span.obf-email");
        Assert.Equal("hello", span.GetAttribute("data-u"));
        Assert.Equal("example.com", span.GetAttribute("data-d"));
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_ContainsNoPlaintextMailto()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "secret@domain.org"));

        // No mailto: URI may appear in the static HTML — JS assembles it client-side.
        Assert.DoesNotContain("mailto:", cut.Markup);
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_DoesNotContainFullEmailAddress()
    {
        const string email = "contact@atelier.test";
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, email));

        // The full assembled address must not appear in the raw HTML served to crawlers —
        // only the split halves live in data-u / data-d.
        Assert.DoesNotContain(email, cut.Markup);
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_RendersEmptySpanPlaceholder()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "user@host.de"));

        // Before JS runs the span is empty; email-deobfuscate.js fills it on load / enhancedload.
        var span = cut.Find("span.obf-email");
        Assert.Equal(string.Empty, span.InnerHtml.Trim());
    }

    [Fact]
    public void EmailObfuscated_StaticMarkup_HasNoAssembledAtSign()
    {
        const string email = "x@y.com";
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, email));

        var span = cut.Find("span.obf-email");
        Assert.Equal("x", span.GetAttribute("data-u"));
        Assert.Equal("y.com", span.GetAttribute("data-d"));
        // The assembled address (with "@") must not be present in the markup.
        Assert.DoesNotContain(email, cut.Markup);
    }

    [Fact]
    public void EmailObfuscated_HasAriaLabel()
    {
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "a@b.com"));

        var span = cut.Find("span.obf-email");
        var label = span.GetAttribute("aria-label") ?? "";
        Assert.False(string.IsNullOrWhiteSpace(label));
    }

    [Fact]
    public void EmailObfuscated_MalformedEmail_RendersEmptyDataAttributes()
    {
        // No "@" → split yields no user/domain; the span carries empty data-attributes (no crash).
        var cut = RenderComponent<EmailObfuscated>(p => p.Add(c => c.Email, "notanemail"));

        var span = cut.Find("span.obf-email");
        Assert.Equal("", span.GetAttribute("data-u"));
        Assert.Equal("", span.GetAttribute("data-d"));
    }
}
