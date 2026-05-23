using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260531120000_Step33StudioSettings")]
    public partial class Step33StudioSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""StudioSettings"" (
    ""Id""        uuid         NOT NULL,
    ""Provider""  varchar(100) NOT NULL DEFAULT '',
    ""Model""     varchar(200) NOT NULL DEFAULT '',
    ""MaxTokens"" integer      NOT NULL DEFAULT 0,
    ""UpdatedAt"" timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ""PK_StudioSettings"" PRIMARY KEY (""Id"")
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""StudioSettings"";");
        }
    }
}
