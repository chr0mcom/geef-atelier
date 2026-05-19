using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260525120000_Step25RunWordCountProviderName")]
    public partial class Step25RunWordCountProviderName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── New columns ──────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    ADD COLUMN IF NOT EXISTS ""WordCount"" integer NULL;

ALTER TABLE ""IterationActorCosts""
    ADD COLUMN IF NOT EXISTS ""ProviderName"" text NULL;

ALTER TABLE ""FinalizationActorCosts""
    ADD COLUMN IF NOT EXISTS ""ProviderName"" text NULL;
");

            // ── Backfill Runs.WordCount from FinalText ───────────────────────────────
            migrationBuilder.Sql(@"
UPDATE ""Runs""
SET ""WordCount"" = (
    CASE
        WHEN ""FinalText"" IS NULL OR trim(""FinalText"") = '' THEN 0
        ELSE array_length(regexp_split_to_array(trim(""FinalText""), E'\\s+'), 1)
    END
)
WHERE ""WordCount"" IS NULL;
");

            // ── Backfill IterationActorCosts.ProviderName ────────────────────────────
            // Tier 1: exact JSONB lateral match from CrewSnapshot
            migrationBuilder.Sql(@"
UPDATE ""IterationActorCosts"" iac
SET ""ProviderName"" = matched.provider
FROM (
    SELECT
        iac2.""Id"",
        COALESCE(
            -- executor match
            (r.""CrewSnapshot"" -> 'executor' ->> 'provider'),
            -- reviewer match
            (SELECT rv ->> 'provider'
             FROM jsonb_array_elements(r.""CrewSnapshot"" -> 'reviewers') rv
             WHERE rv ->> 'name' = iac2.""ActorName""
             LIMIT 1),
            -- advisor match
            (SELECT av ->> 'provider'
             FROM jsonb_array_elements(r.""CrewSnapshot"" -> 'advisors') av
             WHERE av ->> 'name' = iac2.""ActorName""
             LIMIT 1)
        ) AS provider
    FROM ""IterationActorCosts"" iac2
    JOIN ""Iterations"" it ON it.""Id"" = iac2.""IterationId""
    JOIN ""Runs"" r ON r.""Id"" = it.""RunId""
    WHERE r.""CrewSnapshot"" IS NOT NULL
      AND iac2.""ProviderName"" IS NULL
) matched
WHERE iac.""Id"" = matched.""Id""
  AND matched.provider IS NOT NULL;
");

            // Tier 2: heuristic fallback from ModelName prefix
            migrationBuilder.Sql(@"
UPDATE ""IterationActorCosts""
SET ""ProviderName"" = CASE split_part(""ModelName"", '/', 1)
    WHEN 'anthropic' THEN 'claude-cli'
    WHEN 'openai'    THEN 'codex-cli'
    WHEN 'google'    THEN 'gemini-cli'
    ELSE                  'openrouter'
END
WHERE ""ProviderName"" IS NULL
  AND ""ModelName"" LIKE '%/%';
");

            // ── Backfill FinalizationActorCosts.ProviderName ─────────────────────────
            // Tier 1: JSONB lateral match from CrewSnapshot (finalizers array)
            migrationBuilder.Sql(@"
UPDATE ""FinalizationActorCosts"" fac
SET ""ProviderName"" = matched.provider
FROM (
    SELECT
        fac2.""Id"",
        COALESCE(
            -- finalizer match by name
            (SELECT fn ->> 'provider'
             FROM jsonb_array_elements(r.""CrewSnapshot"" -> 'finalizers') fn
             WHERE fn ->> 'name' = fac2.""ActorName""
             LIMIT 1),
            -- fallback to executor provider for transform finalizers
            (r.""CrewSnapshot"" -> 'executor' ->> 'provider')
        ) AS provider
    FROM ""FinalizationActorCosts"" fac2
    JOIN ""Runs"" r ON r.""Id"" = fac2.""RunId""
    WHERE r.""CrewSnapshot"" IS NOT NULL
      AND fac2.""ProviderName"" IS NULL
) matched
WHERE fac.""Id"" = matched.""Id""
  AND matched.provider IS NOT NULL;
");

            // Tier 2: heuristic fallback from ModelName prefix
            migrationBuilder.Sql(@"
UPDATE ""FinalizationActorCosts""
SET ""ProviderName"" = CASE split_part(""ModelName"", '/', 1)
    WHEN 'anthropic' THEN 'claude-cli'
    WHEN 'openai'    THEN 'codex-cli'
    WHEN 'google'    THEN 'gemini-cli'
    ELSE                  'openrouter'
END
WHERE ""ProviderName"" IS NULL
  AND ""ModelName"" LIKE '%/%';
");

            // ── Performance indexes ──────────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_Runs_CreatedAt""
    ON ""Runs"" (""CreatedAt"");

CREATE INDEX IF NOT EXISTS ""IX_Runs_Status_CompletedAt""
    ON ""Runs"" (""Status"", ""CompletedAt"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Runs_Status_CompletedAt"";
DROP INDEX IF EXISTS ""IX_Runs_CreatedAt"";

ALTER TABLE ""FinalizationActorCosts""
    DROP COLUMN IF EXISTS ""ProviderName"";

ALTER TABLE ""IterationActorCosts""
    DROP COLUMN IF EXISTS ""ProviderName"";

ALTER TABLE ""Runs""
    DROP COLUMN IF EXISTS ""WordCount"";
");
        }
    }
}
