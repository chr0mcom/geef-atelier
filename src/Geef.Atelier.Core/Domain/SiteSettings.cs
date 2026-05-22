namespace Geef.Atelier.Core.Domain;

/// <summary>Operator contact data displayed on public legal pages. Exactly one row exists in the database.</summary>
public sealed record SiteSettings
{
    public Guid Id { get; init; }
    public string OperatorName { get; init; } = "";
    public string AddressStreet { get; init; } = "";
    public string AddressZip { get; init; } = "";
    public string AddressCity { get; init; } = "";
    public string AddressCountry { get; init; } = "";
    public string ContactEmail { get; init; } = "";
    public string? ContactPhone { get; init; }
    /// <summary>Person responsible for editorial content (V.i.S.d.P.).</summary>
    public string? ResponsiblePerson { get; init; }
    public string? VatId { get; init; }
    public string? RegisterInfo { get; init; }
    public string? SupervisoryAuthority { get; init; }
    public string? Jurisdiction { get; init; }
    /// <summary>Optional operator-specific Markdown appended after the Privacy policy boilerplate.</summary>
    public string? PrivacyAppendMarkdown { get; init; }
    /// <summary>Optional operator-specific Markdown appended after the Terms boilerplate.</summary>
    public string? TermsAppendMarkdown { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
