using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds <c>tool_definitions</c> and <c>tool_invocations</c> tables for the tool-system feature (A-T2).
    /// <list type="bullet">
    ///   <item><c>tool_definitions</c> — catalogue of tools available to LLM actors; PK is the kebab-case tool name.</item>
    ///   <item><c>tool_invocations</c> — immutable audit log of each tool call made during a run.</item>
    /// </list>
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260610120000_Step37ToolSystem")]
    public partial class Step37ToolSystem : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE tool_definitions (
    ""Name""          character varying(200)  NOT NULL,
    ""DisplayName""   character varying(200)  NOT NULL,
    ""Description""   text                    NOT NULL,
    ""ToolType""      character varying(64)   NOT NULL,
    ""Settings""      jsonb                   NOT NULL DEFAULT '{}'::jsonb,
    ""SecretRef""     character varying(200)  NULL,
    ""LlmSchemaJson"" jsonb                   NOT NULL DEFAULT '{}'::jsonb,
    ""AccessClass""   integer                 NOT NULL,
    ""IsSystem""      boolean                 NOT NULL DEFAULT FALSE,
    CONSTRAINT ""PK_tool_definitions"" PRIMARY KEY (""Name"")
);
");

            migrationBuilder.Sql(@"
CREATE TABLE tool_invocations (
    ""Id""              uuid                    NOT NULL,
    ""RunId""           uuid                    NOT NULL,
    ""IterationNumber"" integer                 NOT NULL,
    ""ActorType""       character varying(100)  NOT NULL,
    ""ActorName""       character varying(100)  NOT NULL,
    ""ToolName""        character varying(100)  NOT NULL,
    ""ToolType""        character varying(100)  NOT NULL,
    ""InputJson""       text                    NOT NULL,
    ""OutputExcerpt""   text                    NULL,
    ""CostEur""         numeric(10,6)           NULL,
    ""DurationMs""      integer                 NOT NULL,
    ""Sequence""        integer                 NOT NULL,
    ""Outcome""         integer                 NOT NULL,
    ""CreatedAt""       timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_tool_invocations"" PRIMARY KEY (""Id"")
);

CREATE INDEX ""IX_tool_invocations_RunId""
    ON tool_invocations (""RunId"");

CREATE UNIQUE INDEX ""IX_tool_invocations_RunId_Sequence""
    ON tool_invocations (""RunId"", ""Sequence"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS tool_invocations;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS tool_definitions;");
        }
    }
}
