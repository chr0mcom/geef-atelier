using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260604130000_Step35AutoCrew")]
    public partial class Step35AutoCrew : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""ParentCompositionRunId"" uuid NULL;
CREATE INDEX IF NOT EXISTS ""IX_Runs_ParentCompositionRunId"" ON ""Runs""(""ParentCompositionRunId"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Runs_ParentCompositionRunId"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""ParentCompositionRunId"";
");
        }
    }
}
