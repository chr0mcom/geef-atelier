using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260516120000_Step16CostTracking")]
    public partial class Step16CostTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Extend Iterations with cost fields
                ALTER TABLE ""Iterations""
                    ADD COLUMN IF NOT EXISTS ""ExecutorInputTokens""   integer       NULL,
                    ADD COLUMN IF NOT EXISTS ""ExecutorOutputTokens""  integer       NULL,
                    ADD COLUMN IF NOT EXISTS ""ExecutorCostEur""       numeric(10,6) NULL,
                    ADD COLUMN IF NOT EXISTS ""ReviewersTotalCostEur"" numeric(10,6) NULL,
                    ADD COLUMN IF NOT EXISTS ""AdvisorsTotalCostEur""  numeric(10,6) NULL;

                -- Extend Runs with cost aggregates
                ALTER TABLE ""Runs""
                    ADD COLUMN IF NOT EXISTS ""TotalCostEur""    numeric(10,6) NULL,
                    ADD COLUMN IF NOT EXISTS ""LlmCostEur""      numeric(10,6) NULL,
                    ADD COLUMN IF NOT EXISTS ""GroundingCostEur"" numeric(10,6) NULL;

                -- Actor-level cost detail table
                CREATE TABLE IF NOT EXISTS ""IterationActorCosts"" (
                    ""Id""           uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""IterationId""  uuid          NOT NULL REFERENCES ""Iterations""(""Id"") ON DELETE CASCADE,
                    ""ActorType""    integer       NOT NULL,
                    ""ActorName""    text          NOT NULL,
                    ""ModelName""    text          NOT NULL,
                    ""InputTokens""  integer       NOT NULL,
                    ""OutputTokens"" integer       NOT NULL,
                    ""CostEur""      numeric(10,6) NULL,
                    ""CreatedAt""    timestamptz   NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ""IX_IterationActorCosts_IterationId""
                    ON ""IterationActorCosts""(""IterationId"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""IterationActorCosts"";

                ALTER TABLE ""Runs""
                    DROP COLUMN IF EXISTS ""TotalCostEur"",
                    DROP COLUMN IF EXISTS ""LlmCostEur"",
                    DROP COLUMN IF EXISTS ""GroundingCostEur"";

                ALTER TABLE ""Iterations""
                    DROP COLUMN IF EXISTS ""ExecutorInputTokens"",
                    DROP COLUMN IF EXISTS ""ExecutorOutputTokens"",
                    DROP COLUMN IF EXISTS ""ExecutorCostEur"",
                    DROP COLUMN IF EXISTS ""ReviewersTotalCostEur"",
                    DROP COLUMN IF EXISTS ""AdvisorsTotalCostEur"";
            ");
        }
    }
}
