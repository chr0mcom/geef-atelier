using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.SiteSettings;
using Geef.Atelier.Web.Components.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;
using DomainSiteSettings = Geef.Atelier.Core.Domain.SiteSettings;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class SiteSettingsAdminTests : TestContext
{
    private sealed class StubSiteSettingsService(DomainSiteSettings settings) : ISiteSettingsService
    {
        public DomainSiteSettings? LastSaved { get; private set; }

        public Task<DomainSiteSettings> GetAsync(CancellationToken ct = default)
            => Task.FromResult(settings);

        public Task UpdateAsync(DomainSiteSettings s, CancellationToken ct = default)
        {
            LastSaved = s;
            return Task.CompletedTask;
        }
    }

    private static DomainSiteSettings DefaultSettings() => new()
    {
        Id = Guid.NewGuid(),
        OperatorName = "Test GmbH",
        AddressStreet = "Testgasse 5",
        AddressZip = "10115",
        AddressCity = "Berlin",
        AddressCountry = "Deutschland",
        ContactEmail = "info@test.de",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private StubSiteSettingsService SetupServices(DomainSiteSettings? settings = null)
    {
        var stub = new StubSiteSettingsService(settings ?? DefaultSettings());
        Services.AddSingleton<ISiteSettingsService>(stub);
        this.AddTestAuthorization().SetAuthorized("admin", AuthorizationState.Authorized);
        return stub;
    }

    [Fact]
    public void SiteSettingsAdmin_RendersPageContainer()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        cut.Find("[data-testid='site-settings-page']");
    }

    [Fact]
    public void SiteSettingsAdmin_RendersForm()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        cut.Find("[data-testid='settings-form']");
    }

    [Fact]
    public void SiteSettingsAdmin_PopulatesOperatorNameField()
    {
        SetupServices(DefaultSettings() with { OperatorName = "Populated GmbH" });

        var cut = RenderComponent<SiteSettingsAdmin>();

        var input = cut.Find("[data-testid='input-operator-name']");
        Assert.Equal("Populated GmbH", input.GetAttribute("value"));
    }

    [Fact]
    public void SiteSettingsAdmin_PopulatesContactEmailField()
    {
        SetupServices(DefaultSettings() with { ContactEmail = "loaded@example.org" });

        var cut = RenderComponent<SiteSettingsAdmin>();

        var input = cut.Find("[data-testid='input-contact-email']");
        Assert.Equal("loaded@example.org", input.GetAttribute("value"));
    }

    [Fact]
    public void SiteSettingsAdmin_ShowsLegalNotice()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        cut.Find(".settings-notice");
    }

    [Fact]
    public void SiteSettingsAdmin_SaveButton_IsPresent()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        var btn = cut.Find("[data-testid='btn-save']");
        Assert.Equal("Speichern", btn.TextContent.Trim());
    }

    [Fact]
    public async Task SiteSettingsAdmin_EmptyOperatorName_ShowsValidationError()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        // Clear operator name field
        var input = cut.Find("[data-testid='input-operator-name']");
        input.Change("");

        // Submit form
        await cut.Find("[data-testid='settings-form']").SubmitAsync();

        cut.Find("[data-testid='validation-error']");
    }

    [Fact]
    public async Task SiteSettingsAdmin_InvalidEmail_ShowsValidationError()
    {
        SetupServices();

        var cut = RenderComponent<SiteSettingsAdmin>();

        var emailInput = cut.Find("[data-testid='input-contact-email']");
        emailInput.Change("not-an-email");

        await cut.Find("[data-testid='settings-form']").SubmitAsync();

        cut.Find("[data-testid='validation-error']");
    }
}
