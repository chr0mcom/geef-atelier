using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260513140000_Step12CliProviderSplit")]
    public partial class Step12CliProviderSplit : Migration
    {
        private const string CodexPattern = @"^(gpt|o[0-9]|openai/)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -- Profile tables: map Provider='cli' to 'claude-cli' or 'codex-cli' by model name --

            foreach (var table in new[] { "ReviewerProfiles", "ExecutorProfiles", "AdvisorProfiles" })
            {
                migrationBuilder.Sql($@"
                    UPDATE ""{table}""
                    SET ""Provider"" = CASE
                        WHEN ""Model"" ~* '{CodexPattern}' THEN 'codex-cli'
                        ELSE 'claude-cli'
                    END
                    WHERE ""Provider"" = 'cli';
                ");
            }

            // -- Runs.CrewSnapshot (JSONB stored as camelCase JSON) --
            // Strategy: two passes ordered codex-first so the fallback (claude-cli) captures
            // claude and unknown models. Mixed-CLI snapshots (executor=claude, reviewer=codex)
            // are not supported by this SQL approach; in practice they do not exist in this
            // project at the time of this migration (system crew uses openrouter exclusively).

            // Pass A: Snapshots that contain codex-type model strings → replace with codex-cli
            migrationBuilder.Sql(@"
                UPDATE ""Runs""
                SET ""CrewSnapshot"" = REPLACE(
                    ""CrewSnapshot""::text,
                    '""provider"":""cli""',
                    '""provider"":""codex-cli""'
                )::jsonb
                WHERE ""CrewSnapshot"" IS NOT NULL
                  AND ""CrewSnapshot""::text LIKE '%""provider"":""cli""%'
                  AND ""CrewSnapshot""::text ~* '""model"":""(gpt|o[0-9]|openai/)';
            ");

            // Pass B: Remaining snapshots (claude models + unknowns) → replace with claude-cli
            migrationBuilder.Sql(@"
                UPDATE ""Runs""
                SET ""CrewSnapshot"" = REPLACE(
                    ""CrewSnapshot""::text,
                    '""provider"":""cli""',
                    '""provider"":""claude-cli""'
                )::jsonb
                WHERE ""CrewSnapshot"" IS NOT NULL
                  AND ""CrewSnapshot""::text LIKE '%""provider"":""cli""%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: remap claude-cli and codex-cli back to cli in profile tables
            foreach (var table in new[] { "ReviewerProfiles", "ExecutorProfiles", "AdvisorProfiles" })
            {
                migrationBuilder.Sql($@"
                    UPDATE ""{table}""
                    SET ""Provider"" = 'cli'
                    WHERE ""Provider"" IN ('claude-cli', 'codex-cli');
                ");
            }

            // Reverse CrewSnapshot JSON (both specific → generic)
            migrationBuilder.Sql(@"
                UPDATE ""Runs""
                SET ""CrewSnapshot"" = REPLACE(
                    REPLACE(""CrewSnapshot""::text,
                        '""provider"":""claude-cli""',
                        '""provider"":""cli""'),
                    '""provider"":""codex-cli""',
                    '""provider"":""cli""'
                )::jsonb
                WHERE ""CrewSnapshot"" IS NOT NULL
                  AND (""CrewSnapshot""::text LIKE '%""provider"":""claude-cli""%'
                    OR ""CrewSnapshot""::text LIKE '%""provider"":""codex-cli""%');
            ");
        }
    }
}
