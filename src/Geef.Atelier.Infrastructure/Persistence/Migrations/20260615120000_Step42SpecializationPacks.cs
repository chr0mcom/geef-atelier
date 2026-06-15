using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>specialization_packs</c> table and the <c>ActorPackBindings</c> column on
    /// <c>CrewTemplates</c> for the generic-actors + specialization-packs feature, and performs the
    /// clean reseed mandated by the concept (§12): custom profiles, crews, snapshots and run history
    /// are cleared so the new generic system actors + packs become the baseline. No backwards-compat
    /// read path. System actors, packs and templates are code constants (reseeded from code).
    /// Kept: users, providers, settings, OAuth, system tools, the knowledge base (global documents),
    /// approved learnings and MCP servers. This data wipe is irreversible (Down only drops the schema).
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260615120000_Step42SpecializationPacks")]
    public partial class Step42SpecializationPacks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. New schema ───────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE specialization_packs (
    ""Name""                 character varying(200)  NOT NULL,
    ""DisplayName""          character varying(200)  NOT NULL,
    ""Description""          text                    NOT NULL,
    ""SpecializationText""   text                    NOT NULL,
    ""Scope""                integer                 NOT NULL,
    ""Domain""               character varying(100)  NULL,
    ""ApplicableActorTypes"" jsonb                   NOT NULL DEFAULT '[]'::jsonb,
    ""OwningCrewId""         character varying(200)  NULL,
    ""IsSystem""             boolean                 NOT NULL DEFAULT FALSE,
    ""Archived""             boolean                 NOT NULL DEFAULT FALSE,
    ""CreatedAt""            timestamp with time zone NULL,
    ""UpdatedAt""            timestamp with time zone NULL,
    ""LastUsedAt""           timestamp with time zone NULL,
    CONSTRAINT ""PK_specialization_packs"" PRIMARY KEY (""Name"")
);

CREATE INDEX ""IX_specialization_packs_OwningCrewId"" ON specialization_packs (""OwningCrewId"");
CREATE INDEX ""IX_specialization_packs_Scope_Archived"" ON specialization_packs (""Scope"", ""Archived"");
");

            migrationBuilder.Sql(@"
ALTER TABLE ""CrewTemplates""
    ADD COLUMN ""ActorPackBindings"" jsonb NOT NULL DEFAULT '{}'::jsonb;
");

            // ── 2. Clean reseed (concept §12): clear custom crews/profiles/runs ─────
            // Ordered DELETEs (child → parent) so existing foreign keys are respected. Run-local
            // knowledge attachments are removed (they belong to deleted runs); global knowledge and
            // learnings are preserved. IMPORTANT: profile/template tables only drop CUSTOM rows
            // ("IsSystem" = false). System finalizers and grounding providers are DB-seeded by earlier
            // migrations (Step15/Step22/Step30) and must survive — deleting them would orphan the
            // system catalogue permanently (no reseed path).
            migrationBuilder.Sql(@"
DELETE FROM ""FinalizationActorCosts"";
DELETE FROM ""GroundingActorCosts"";
DELETE FROM ""IterationActorCosts"";
DELETE FROM ""GroundingConsultations"";
DELETE FROM ""AdvisorConsultations"";
DELETE FROM tool_invocations;
DELETE FROM ""RunArtifacts"";
DELETE FROM ""Events"";
DELETE FROM ""Findings"";
DELETE FROM ""KnowledgeDocuments"" WHERE ""RunId"" IS NOT NULL;
DELETE FROM ""Iterations"";
DELETE FROM ""Runs"";

DELETE FROM ""CrewTemplateEmbeddings"";
DELETE FROM ""TemplateStudioAnalyses"";
DELETE FROM ""ReviewerProfiles""          WHERE ""IsSystem"" = false;
DELETE FROM ""ExecutorProfiles""          WHERE ""IsSystem"" = false;
DELETE FROM ""AdvisorProfiles""           WHERE ""IsSystem"" = false;
DELETE FROM ""GroundingProviderProfiles"" WHERE ""IsSystem"" = false;
DELETE FROM ""FinalizerProfiles""         WHERE ""IsSystem"" = false;
DELETE FROM ""CrewTemplates""             WHERE ""IsSystem"" = false;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The data wipe is irreversible; Down only reverses the schema changes.
            migrationBuilder.Sql(@"ALTER TABLE ""CrewTemplates"" DROP COLUMN IF EXISTS ""ActorPackBindings"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS specialization_packs;");
        }
    }
}
