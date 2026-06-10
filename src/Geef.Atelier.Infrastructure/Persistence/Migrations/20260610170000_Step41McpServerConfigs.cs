using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds <c>mcp_server_configs</c> table for the tool-system MCP client feature (C-T1).
    /// Stores registered outbound MCP server endpoints (URL + optional auth env-var key).
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260610170000_Step41McpServerConfigs")]
    public partial class Step41McpServerConfigs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS mcp_server_configs (
    ""Id""            uuid            NOT NULL PRIMARY KEY,
    ""Name""          varchar(200)    NOT NULL,
    ""Url""           varchar(500)    NOT NULL,
    ""AuthHeaderEnv"" varchar(200)    NULL,
    ""IsActive""      boolean         NOT NULL DEFAULT true,
    ""UpdatedAt""     timestamptz     NOT NULL DEFAULT NOW()
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS mcp_server_configs;");
        }
    }
}
