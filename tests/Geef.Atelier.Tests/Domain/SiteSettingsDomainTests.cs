using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Tests.Domain;

public sealed class SiteSettingsDomainTests
{
    [Fact]
    public void SiteSettings_HasAllRequiredFields()
    {
        var settings = new SiteSettings
        {
            Id = Guid.NewGuid(),
            OperatorName = "Test GmbH",
            AddressStreet = "Teststraße 1",
            AddressZip = "12345",
            AddressCity = "Teststadt",
            AddressCountry = "Deutschland",
            ContactEmail = "test@example.com",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal("Test GmbH", settings.OperatorName);
        Assert.Equal("Teststraße 1", settings.AddressStreet);
        Assert.Equal("12345", settings.AddressZip);
        Assert.Equal("Teststadt", settings.AddressCity);
        Assert.Equal("Deutschland", settings.AddressCountry);
        Assert.Equal("test@example.com", settings.ContactEmail);
    }

    [Fact]
    public void SiteSettings_OptionalFieldsDefaultToNull()
    {
        var settings = new SiteSettings();

        Assert.Null(settings.ContactPhone);
        Assert.Null(settings.ResponsiblePerson);
        Assert.Null(settings.VatId);
        Assert.Null(settings.RegisterInfo);
        Assert.Null(settings.SupervisoryAuthority);
        Assert.Null(settings.Jurisdiction);
        Assert.Null(settings.PrivacyAppendMarkdown);
        Assert.Null(settings.TermsAppendMarkdown);
    }

    [Fact]
    public void SiteSettings_IsImmutableRecord()
    {
        var original = new SiteSettings { OperatorName = "Original", ContactEmail = "a@b.com" };
        var updated = original with { OperatorName = "Updated" };

        Assert.Equal("Original", original.OperatorName);
        Assert.Equal("Updated", updated.OperatorName);
        Assert.Equal("a@b.com", updated.ContactEmail);
    }
}
