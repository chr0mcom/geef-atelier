using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>specialization_packs</c> table and the <c>ActorPackBindings</c> column on
    /// <c>CrewTemplates</c> for the generic-actors + specialization-packs feature, and performs a clean
    /// reset (operator request): keep ONLY user accounts and auth/credentials/config; wipe everything
    /// else so the platform starts fresh with the improved generic system actors + packs.
    /// <para>
    /// KEEP: <c>Users</c>, all OAuth tables, <c>mcp_server_configs</c>, <c>Providers</c> (LLM
    /// credentials), <c>SiteSettings</c>, <c>StudioSettings</c>, and the DB-seeded SYSTEM catalogue
    /// (system tools, system finalizers, system grounding providers — they have no reseed path).
    /// </para>
    /// <para>
    /// WIPE: run history, custom profiles/templates/packs, crew embeddings, studio analyses, the
    /// knowledge base (all documents + chunks), approved learnings, and custom tools. Improved
    /// reviewer/executor/advisor prompts come from code constants (no DB reseed needed).
    /// </para>
    /// This data wipe is irreversible (Down only drops the schema). Take a full pg_dump before deploy.
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

            // ── 2. Clean reset (operator request): keep only accounts + auth/credentials/config ─────
            // Ordered DELETEs (child → parent) so existing foreign keys are respected.
            // Profile/template tables only drop CUSTOM rows ("IsSystem" = false): the system
            // finalizers and grounding providers are DB-seeded by earlier migrations (Step15/22/30)
            // and must survive — deleting them would orphan the system catalogue permanently.
            // The knowledge base, learnings and custom tools are wiped for a fresh start; users,
            // OAuth, MCP servers, providers and settings are NOT touched here (kept in place).
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
DELETE FROM ""KnowledgeDocuments"";
DELETE FROM ""LearningEntries"";
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
DELETE FROM tool_definitions             WHERE ""IsSystem"" = false;
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
