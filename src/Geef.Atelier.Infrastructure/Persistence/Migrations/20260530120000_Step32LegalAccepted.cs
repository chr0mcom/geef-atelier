using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260530120000_Step32LegalAccepted")]
    public partial class Step32LegalAccepted : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""SiteSettings""
    ADD COLUMN IF NOT EXISTS ""LegalBoilerplateAccepted"" boolean NOT NULL DEFAULT false;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""SiteSettings""
    DROP COLUMN IF EXISTS ""LegalBoilerplateAccepted"";
");
        }
    }
}
