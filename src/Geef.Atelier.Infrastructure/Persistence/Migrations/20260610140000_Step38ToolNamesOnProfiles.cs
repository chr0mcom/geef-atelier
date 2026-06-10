using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>ToolNames</c> JSONB column to all four profile tables.
    /// The column stores a JSON array of tool-name strings and defaults to an empty array.
    /// It enables agentic tool-use loops for executor, reviewer, advisor, and transform-finalizer profiles.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260610140000_Step38ToolNamesOnProfiles")]
    public partial class Step38ToolNamesOnProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ExecutorProfiles""
    ADD COLUMN IF NOT EXISTS ""ToolNames"" jsonb DEFAULT '[]'::jsonb;
");

            migrationBuilder.Sql(@"
ALTER TABLE ""ReviewerProfiles""
    ADD COLUMN IF NOT EXISTS ""ToolNames"" jsonb DEFAULT '[]'::jsonb;
");

            migrationBuilder.Sql(@"
ALTER TABLE ""AdvisorProfiles""
    ADD COLUMN IF NOT EXISTS ""ToolNames"" jsonb DEFAULT '[]'::jsonb;
");

            migrationBuilder.Sql(@"
ALTER TABLE ""FinalizerProfiles""
    ADD COLUMN IF NOT EXISTS ""ToolNames"" jsonb DEFAULT '[]'::jsonb;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ExecutorProfiles"" DROP COLUMN IF EXISTS ""ToolNames"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ReviewerProfiles"" DROP COLUMN IF EXISTS ""ToolNames"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AdvisorProfiles"" DROP COLUMN IF EXISTS ""ToolNames"";");
            migrationBuilder.Sql(@"ALTER TABLE ""FinalizerProfiles"" DROP COLUMN IF EXISTS ""ToolNames"";");
        }
    }
}
