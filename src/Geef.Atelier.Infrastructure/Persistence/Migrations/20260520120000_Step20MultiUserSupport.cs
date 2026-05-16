using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260520120000_Step20MultiUserSupport")]
    public partial class Step20MultiUserSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE ""Users"" (
    ""UserId"" text PRIMARY KEY,
    ""Username"" text NOT NULL,
    ""PasswordHash"" text NOT NULL,
    ""Email"" text NULL,
    ""IsActive"" boolean NOT NULL DEFAULT true,
    ""IsAdmin"" boolean NOT NULL DEFAULT false,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now(),
    ""UpdatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ""IX_Users_Username"" ON ""Users""(""Username"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Users_Username"";
DROP TABLE IF EXISTS ""Users"";
");
        }
    }
}
