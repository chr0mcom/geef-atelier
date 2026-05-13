using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260513170000_Step13GroundingProviders")]
    public partial class Step13GroundingProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""GroundingProviderProfiles"" (
                    ""Name""             varchar(200)  PRIMARY KEY,
                    ""DisplayName""      text          NOT NULL,
                    ""Description""      text          NOT NULL,
                    ""ProviderType""     varchar(64)   NOT NULL,
                    ""ProviderSettings"" jsonb         NOT NULL DEFAULT '{}'::jsonb,
                    ""MaxQueriesPerRun"" integer       NULL,
                    ""IsSystem""         boolean       NOT NULL DEFAULT false
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""GroundingConsultations"" (
                    ""Id""                   uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""RunId""                uuid            NOT NULL REFERENCES ""Runs""(""Id"") ON DELETE CASCADE,
                    ""GroundingProviderName"" varchar(200)    NOT NULL,
                    ""Query""                text            NOT NULL,
                    ""Citations""            jsonb           NOT NULL DEFAULT '[]'::jsonb,
                    ""TokensOrCreditsUsed""  integer         NOT NULL DEFAULT 0,
                    ""CostEur""              numeric(10,4)   NULL,
                    ""CreatedAt""            timestamptz     NOT NULL DEFAULT now()
                );
                CREATE INDEX IF NOT EXISTS ""IX_GroundingConsultations_RunId""
                    ON ""GroundingConsultations""(""RunId"");
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""CrewTemplates""
                    ADD COLUMN IF NOT EXISTS ""GroundingProviderNames"" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""CrewTemplates"" DROP COLUMN IF EXISTS ""GroundingProviderNames"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""GroundingConsultations"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""GroundingProviderProfiles"";");
        }
    }
}
