using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260526120000_Step27GroundingActorCosts")]
    public partial class Step27GroundingActorCosts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""GroundingActorCosts"" (
    ""Id"" uuid NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    ""RunId"" uuid NOT NULL REFERENCES ""Runs""(""Id"") ON DELETE CASCADE,
    ""GroundingProviderName"" text NOT NULL,
    ""ActorName"" text NOT NULL,
    ""ProviderName"" text NULL,
    ""ModelName"" text NULL,
    ""InputTokens"" integer NOT NULL DEFAULT 0,
    ""OutputTokens"" integer NOT NULL DEFAULT 0,
    ""CostEur"" numeric(12,6) NULL,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ""IX_GroundingActorCosts_RunId"" ON ""GroundingActorCosts""(""RunId"");

ALTER TABLE ""GroundingConsultations""
    ADD COLUMN IF NOT EXISTS ""RefinementOutcome"" jsonb NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""GroundingConsultations"" DROP COLUMN IF EXISTS ""RefinementOutcome"";
DROP TABLE IF EXISTS ""GroundingActorCosts"";
");
        }
    }
}
