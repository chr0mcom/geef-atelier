using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260523120000_Step23RunResume")]
    public partial class Step23RunResume : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""ParentRunId"" uuid NULL;
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""SeedDraftText"" text NULL;
CREATE INDEX IF NOT EXISTS ""IX_Runs_ParentRunId"" ON ""Runs""(""ParentRunId"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Runs_ParentRunId"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""ParentRunId"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""SeedDraftText"";");
        }
    }
}
