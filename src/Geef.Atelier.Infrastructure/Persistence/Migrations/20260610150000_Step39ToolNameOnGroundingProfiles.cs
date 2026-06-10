using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the nullable <c>ToolName</c> column to <c>GroundingProviderProfiles</c>.
    /// When set, the pipeline uses <c>ToolBackedGroundingProvider</c> instead of the legacy
    /// <c>ProviderType</c>-based factory path. Existing rows remain unaffected (column is nullable).
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260610150000_Step39ToolNameOnGroundingProfiles")]
    public partial class Step39ToolNameOnGroundingProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "GroundingProviderProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "GroundingProviderProfiles");
        }
    }
}
