namespace Geef.Atelier.Infrastructure.Persistence.SiteSettings;

internal sealed class SiteSettingsEntity
{
    public Guid Id { get; set; }
    public string OperatorName { get; set; } = "";
    public string AddressStreet { get; set; } = "";
    public string AddressZip { get; set; } = "";
    public string AddressCity { get; set; } = "";
    public string AddressCountry { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string? ContactPhone { get; set; }
    public string? ResponsiblePerson { get; set; }
    public string? VatId { get; set; }
    public string? RegisterInfo { get; set; }
    public string? SupervisoryAuthority { get; set; }
    public string? Jurisdiction { get; set; }
    public string? PrivacyAppendMarkdown { get; set; }
    public string? TermsAppendMarkdown { get; set; }
    public bool LegalBoilerplateAccepted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
