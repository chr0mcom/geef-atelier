using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260529120000_Step31PublicPages")]
    public partial class Step31PublicPages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SiteSettings"" (
    ""Id""                    uuid         PRIMARY KEY,
    ""OperatorName""          varchar(200) NOT NULL DEFAULT '',
    ""AddressStreet""         varchar(300) NOT NULL DEFAULT '',
    ""AddressZip""            varchar(20)  NOT NULL DEFAULT '',
    ""AddressCity""           varchar(100) NOT NULL DEFAULT '',
    ""AddressCountry""        varchar(100) NOT NULL DEFAULT '',
    ""ContactEmail""          varchar(300) NOT NULL DEFAULT '',
    ""ContactPhone""          varchar(100)     NULL,
    ""ResponsiblePerson""     varchar(300)     NULL,
    ""VatId""                 varchar(50)      NULL,
    ""RegisterInfo""          varchar(300)     NULL,
    ""SupervisoryAuthority""  varchar(300)     NULL,
    ""Jurisdiction""          varchar(100)     NULL,
    ""PrivacyAppendMarkdown"" text             NULL,
    ""TermsAppendMarkdown""   text             NULL,
    ""UpdatedAt""             timestamptz  NOT NULL DEFAULT now()
);

INSERT INTO ""SiteSettings"" (
    ""Id"", ""OperatorName"", ""AddressStreet"", ""AddressZip"",
    ""AddressCity"", ""AddressCountry"", ""ContactEmail"", ""UpdatedAt""
) VALUES (
    '00000000-0000-0000-0000-000000000001',
    '[Bitte ausfüllen]',
    '[Straße und Hausnummer]',
    '[PLZ]',
    '[Stadt]',
    'Deutschland',
    'kontakt@example.com',
    now()
) ON CONFLICT DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SiteSettings"";");
        }
    }
}
