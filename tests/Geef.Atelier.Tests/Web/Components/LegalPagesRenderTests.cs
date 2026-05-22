using Bunit;
using Geef.Atelier.Application.SiteSettings;
using Geef.Atelier.Web.Components.Pages.Public;
using Microsoft.Extensions.DependencyInjection;
using DomainSiteSettings = Geef.Atelier.Core.Domain.SiteSettings;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class LegalPagesRenderTests : TestContext
{
    private sealed class StubSiteSettingsService(DomainSiteSettings settings) : ISiteSettingsService
    {
        public Task<DomainSiteSettings> GetAsync(CancellationToken ct = default)
            => Task.FromResult(settings);

        public Task UpdateAsync(DomainSiteSettings s, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static DomainSiteSettings MakeSettings(
        string operatorName = "Mustermann GmbH",
        string email = "kontakt@example.com",
        string phone = "+49 30 123456",
        string vatId = "DE123456789",
        string registerInfo = "HRB 12345 AG Musterstadt") => new()
    {
        Id = Guid.NewGuid(),
        OperatorName = operatorName,
        AddressStreet = "Musterstraße 1",
        AddressZip = "12345",
        AddressCity = "Musterstadt",
        AddressCountry = "Deutschland",
        ContactEmail = email,
        ContactPhone = phone,
        VatId = vatId,
        RegisterInfo = registerInfo,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private void RegisterSettings(DomainSiteSettings settings)
        => Services.AddSingleton<ISiteSettingsService>(new StubSiteSettingsService(settings));

    // ── Imprint ───────────────────────────────────────────────────────────

    [Fact]
    public void Imprint_ShowsOperatorName()
    {
        RegisterSettings(MakeSettings(operatorName: "Testfirma AG"));

        var cut = RenderComponent<Imprint>();

        Assert.Contains("Testfirma AG", cut.Markup);
    }

    [Fact]
    public void Imprint_ContainsLegalDisclaimerBanner()
    {
        RegisterSettings(MakeSettings());

        var cut = RenderComponent<Imprint>();

        cut.Find(".legal-disclaimer");
    }

    [Fact]
    public void Imprint_ShowsAddressCity()
    {
        RegisterSettings(MakeSettings());

        var cut = RenderComponent<Imprint>();

        Assert.Contains("Musterstadt", cut.Markup);
    }

    [Fact]
    public void Imprint_ShowsOptionalVatId_WhenProvided()
    {
        RegisterSettings(MakeSettings(vatId: "DE999888777"));

        var cut = RenderComponent<Imprint>();

        Assert.Contains("DE999888777", cut.Markup);
    }

    [Fact]
    public void Imprint_OmitsVatId_WhenNull()
    {
        var s = MakeSettings() with { VatId = null };
        RegisterSettings(s);

        var cut = RenderComponent<Imprint>();

        Assert.DoesNotContain("Umsatzsteuer", cut.Markup);
    }

    [Fact]
    public void Imprint_EmailRenderedWithObfuscation_NotPlaintext()
    {
        RegisterSettings(MakeSettings(email: "secret@legal.test"));

        var cut = RenderComponent<Imprint>();

        // Full address must not appear as a mailto: URI in static HTML
        Assert.DoesNotContain("mailto:secret@legal.test", cut.Markup);
        // But the obfuscation anchor must be present
        cut.Find("a.obf-email");
    }

    // ── Privacy ───────────────────────────────────────────────────────────

    [Fact]
    public void Privacy_RendersBoilerplateHeading()
    {
        RegisterSettings(MakeSettings());

        var cut = RenderComponent<Privacy>();

        Assert.Contains("Datenschutz", cut.Markup);
    }

    [Fact]
    public void Privacy_ShowsOperatorName()
    {
        RegisterSettings(MakeSettings(operatorName: "Privacy Corp"));

        var cut = RenderComponent<Privacy>();

        Assert.Contains("Privacy Corp", cut.Markup);
    }

    [Fact]
    public void Privacy_RendersAppendMarkdown_WhenProvided()
    {
        var s = MakeSettings() with { PrivacyAppendMarkdown = "## Custom Section\nCustom text here." };
        RegisterSettings(s);

        var cut = RenderComponent<Privacy>();

        Assert.Contains("Custom Section", cut.Markup);
        Assert.Contains("Custom text here.", cut.Markup);
    }

    [Fact]
    public void Privacy_OmitsAppendSection_WhenNull()
    {
        var s = MakeSettings() with { PrivacyAppendMarkdown = null };
        RegisterSettings(s);

        var cut = RenderComponent<Privacy>();

        Assert.DoesNotContain("legal-append", cut.Markup);
    }

    // ── Terms ─────────────────────────────────────────────────────────────

    [Fact]
    public void Terms_RendersBoilerplateHeading()
    {
        RegisterSettings(MakeSettings());

        var cut = RenderComponent<Terms>();

        Assert.Contains("Nutzungsbedingungen", cut.Markup);
    }

    [Fact]
    public void Terms_RendersAppendMarkdown_WhenProvided()
    {
        var s = MakeSettings() with { TermsAppendMarkdown = "## Extra Clause\nNo robots." };
        RegisterSettings(s);

        var cut = RenderComponent<Terms>();

        Assert.Contains("Extra Clause", cut.Markup);
    }

    // ── Contact ───────────────────────────────────────────────────────────

    [Fact]
    public void Contact_RendersContactCard()
    {
        RegisterSettings(MakeSettings(operatorName: "Contact Corp"));

        var cut = RenderComponent<Contact>();

        cut.Find(".contact-card");
        Assert.Contains("Contact Corp", cut.Markup);
    }

    [Fact]
    public void Contact_EmailRenderedWithObfuscation()
    {
        RegisterSettings(MakeSettings(email: "reach@contact.test"));

        var cut = RenderComponent<Contact>();

        Assert.DoesNotContain("mailto:reach@contact.test", cut.Markup);
        cut.Find("a.obf-email");
    }
}
