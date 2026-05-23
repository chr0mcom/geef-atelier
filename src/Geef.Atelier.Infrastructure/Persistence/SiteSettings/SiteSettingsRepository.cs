using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.SiteSettings;

internal sealed class SiteSettingsRepository(AtelierDbContext db) : ISiteSettingsRepository
{
    private static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task<Core.Domain.SiteSettings> GetAsync(CancellationToken ct = default)
    {
        var entity = await db.SiteSettings.FirstOrDefaultAsync(ct);
        if (entity is null)
        {
            entity = CreateDefault();
            db.SiteSettings.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        return MapToDomain(entity);
    }

    public async Task UpdateAsync(Core.Domain.SiteSettings settings, CancellationToken ct = default)
    {
        var entity = await db.SiteSettings.FirstOrDefaultAsync(ct);
        if (entity is null)
        {
            entity = CreateDefault();
            db.SiteSettings.Add(entity);
        }

        entity.OperatorName = settings.OperatorName;
        entity.AddressStreet = settings.AddressStreet;
        entity.AddressZip = settings.AddressZip;
        entity.AddressCity = settings.AddressCity;
        entity.AddressCountry = settings.AddressCountry;
        entity.ContactEmail = settings.ContactEmail;
        entity.ContactPhone = settings.ContactPhone;
        entity.ResponsiblePerson = settings.ResponsiblePerson;
        entity.VatId = settings.VatId;
        entity.RegisterInfo = settings.RegisterInfo;
        entity.SupervisoryAuthority = settings.SupervisoryAuthority;
        entity.Jurisdiction = settings.Jurisdiction;
        entity.PrivacyAppendMarkdown = settings.PrivacyAppendMarkdown;
        entity.TermsAppendMarkdown = settings.TermsAppendMarkdown;
        entity.LegalBoilerplateAccepted = settings.LegalBoilerplateAccepted;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static SiteSettingsEntity CreateDefault() => new()
    {
        Id = SingletonId,
        OperatorName = "[Bitte ausfüllen]",
        AddressStreet = "[Straße und Hausnummer]",
        AddressZip = "[PLZ]",
        AddressCity = "[Stadt]",
        AddressCountry = "Deutschland",
        ContactEmail = "kontakt@example.com",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Core.Domain.SiteSettings MapToDomain(SiteSettingsEntity e) => new()
    {
        Id = e.Id,
        OperatorName = e.OperatorName,
        AddressStreet = e.AddressStreet,
        AddressZip = e.AddressZip,
        AddressCity = e.AddressCity,
        AddressCountry = e.AddressCountry,
        ContactEmail = e.ContactEmail,
        ContactPhone = e.ContactPhone,
        ResponsiblePerson = e.ResponsiblePerson,
        VatId = e.VatId,
        RegisterInfo = e.RegisterInfo,
        SupervisoryAuthority = e.SupervisoryAuthority,
        Jurisdiction = e.Jurisdiction,
        PrivacyAppendMarkdown = e.PrivacyAppendMarkdown,
        TermsAppendMarkdown = e.TermsAppendMarkdown,
        LegalBoilerplateAccepted = e.LegalBoilerplateAccepted,
        UpdatedAt = e.UpdatedAt,
    };
}
