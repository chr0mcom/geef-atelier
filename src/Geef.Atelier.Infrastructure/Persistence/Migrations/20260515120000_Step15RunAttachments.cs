using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260515120000_Step15RunAttachments")]
    public partial class Step15RunAttachments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""KnowledgeDocuments""
                    ADD COLUMN IF NOT EXISTS ""Scope"" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""RunId"" uuid NULL;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'FK_KnowledgeDocuments_Runs_RunId'
                          AND conrelid = '""KnowledgeDocuments""'::regclass
                    ) THEN
                        ALTER TABLE ""KnowledgeDocuments""
                            ADD CONSTRAINT ""FK_KnowledgeDocuments_Runs_RunId""
                            FOREIGN KEY (""RunId"") REFERENCES ""Runs""(""Id"") ON DELETE CASCADE;
                    END IF;
                END$$;

                CREATE INDEX IF NOT EXISTS ""IX_KnowledgeDocuments_RunId"" ON ""KnowledgeDocuments""(""RunId"")
                    WHERE ""RunId"" IS NOT NULL;

                CREATE INDEX IF NOT EXISTS ""IX_KnowledgeDocuments_Scope"" ON ""KnowledgeDocuments""(""Scope"");

                INSERT INTO ""GroundingProviderProfiles"" (
                    ""Name"", ""DisplayName"", ""Description"", ""ProviderType"", ""ProviderSettings"",
                    ""MaxQueriesPerRun"", ""IsSystem""
                ) VALUES (
                    'run-attachments',
                    'Run Attachments',
                    'Uses documents uploaded with the briefing as a grounding source. Activated automatically when attachments are present.',
                    'vector-store',
                    '{""TopK"": ""5"", ""Scope"": ""run-local""}'::jsonb,
                    1,
                    true
                ) ON CONFLICT (""Name"") DO NOTHING;

                UPDATE ""GroundingProviderProfiles""
                   SET ""ProviderSettings"" = ""ProviderSettings"" || '{""Scope"": ""global""}'::jsonb
                 WHERE ""Name"" = 'knowledge-base-default'
                   AND NOT (""ProviderSettings"" ? 'Scope');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_KnowledgeDocuments_Scope"";
                DROP INDEX IF EXISTS ""IX_KnowledgeDocuments_RunId"";
                ALTER TABLE ""KnowledgeDocuments""
                    DROP CONSTRAINT IF EXISTS ""FK_KnowledgeDocuments_Runs_RunId"",
                    DROP COLUMN IF EXISTS ""RunId"",
                    DROP COLUMN IF EXISTS ""Scope"";
                DELETE FROM ""GroundingProviderProfiles"" WHERE ""Name"" = 'run-attachments';
                UPDATE ""GroundingProviderProfiles""
                   SET ""ProviderSettings"" = ""ProviderSettings"" - 'Scope'
                 WHERE ""Name"" = 'knowledge-base-default';
            ");
        }
    }
}
