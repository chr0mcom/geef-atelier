using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Engine honesty (Etappe 0, package A). Additive and idempotent; every statement can run again on a database
    /// where it already ran, partly or fully:
    /// <list type="number">
    ///   <item><c>RunWarnings</c>: problems a run worked around instead of stopping (skipped sources, failed
    ///   reviewers, replacement models, failovers, skipped finishing steps), cascading with the run.</item>
    ///   <item><c>Runs.Outcome</c> (integer enum, nullable), <c>Runs.DeliveredIteration</c> and <c>Runs.ReportJson</c>.
    ///   Existing runs keep <c>NULL</c> ("finished before Step47").</item>
    ///   <item>Grounding bridge: the five system grounding profiles lose their <c>ToolName</c> so the pipeline takes
    ///   the working provider-type path again (the tool path was dead for knowledge-base/academic-search and read a
    ///   key name the container does not set). System profiles are served from code, so this only clears rows that
    ///   a database might still carry; run snapshots are never touched.</item>
    /// </list>
    /// Literals are frozen on purpose: a migration must not follow later edits of <c>SystemCrew</c>.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261124120000_Step47EngineHonesty")]
    public partial class Step47EngineHonesty : Migration
    {
        internal static readonly string[] LegacyGroundingProfiles =
            ["tavily-refined", "tavily-basic", "run-attachments", "knowledge-base-default", "academic-default"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "RunWarnings" (
    "Id"        uuid                     NOT NULL,
    "RunId"     uuid                     NOT NULL,
    "Iteration" integer                  NULL,
    "Source"    character varying(100)   NOT NULL,
    "Code"      character varying(100)   NOT NULL,
    "Message"   text                     NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_RunWarnings" PRIMARY KEY ("Id")
);
""");

            migrationBuilder.Sql("""
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_RunWarnings_Runs_RunId') THEN
        ALTER TABLE "RunWarnings"
            ADD CONSTRAINT "FK_RunWarnings_Runs_RunId"
            FOREIGN KEY ("RunId") REFERENCES "Runs"("Id") ON DELETE CASCADE;
    END IF;
END $$;
""");

            migrationBuilder.Sql("""
CREATE INDEX IF NOT EXISTS "IX_RunWarnings_RunId" ON "RunWarnings" ("RunId");
""");

            migrationBuilder.Sql("""
ALTER TABLE "Runs"
    ADD COLUMN IF NOT EXISTS "Outcome"            integer NULL,
    ADD COLUMN IF NOT EXISTS "DeliveredIteration" integer NULL,
    ADD COLUMN IF NOT EXISTS "ReportJson"         text    NULL;
""");

            var names = string.Join(", ", LegacyGroundingProfiles.Select(n => $"'{n}'"));
            migrationBuilder.Sql($"""
UPDATE "GroundingProviderProfiles" SET "ToolName" = NULL
WHERE "IsSystem" = true AND "ToolName" IS NOT NULL AND "Name" IN ({names});
""");
        }

        /// <summary>
        /// Drops the additive schema. The <c>ToolName</c> cleanup is intentionally not reverted: the tool path it
        /// pointed to is broken for these profiles.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TABLE IF EXISTS "RunWarnings";
ALTER TABLE "Runs"
    DROP COLUMN IF EXISTS "Outcome",
    DROP COLUMN IF EXISTS "DeliveredIteration",
    DROP COLUMN IF EXISTS "ReportJson";
""");
        }
    }
}
