using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Step10CrewSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CrewSnapshot",
                table: "Runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrewTemplateName",
                table: "Runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrewTemplates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ExecutorProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewerProfileNames = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    EvaluationStrategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConvergenceOverride = table.Column<string>(type: "jsonb", nullable: true),
                    AdvisorProfileNames = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewTemplates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "ExecutorProfiles",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutorProfiles", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "ReviewerProfiles",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewerProfiles", x => x.Name);
                });

            // Backfill: all pre-PS-5 runs were executed with the hardcoded "klassik" crew.
            migrationBuilder.Sql("""
                UPDATE "Runs" SET "CrewTemplateName" = 'klassik' WHERE "CrewTemplateName" IS NULL;
                """);

            // Rename legacy class-based reviewer names to canonical kebab-case slugs.
            migrationBuilder.Sql("""
                UPDATE "Findings" SET "ReviewerName" = 'briefing-fidelity' WHERE "ReviewerName" = 'BriefingTreueReviewer';
                UPDATE "Findings" SET "ReviewerName" = 'clarity'           WHERE "ReviewerName" = 'KlarheitReviewer';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrewTemplates");

            migrationBuilder.DropTable(
                name: "ExecutorProfiles");

            migrationBuilder.DropTable(
                name: "ReviewerProfiles");

            migrationBuilder.DropColumn(
                name: "CrewSnapshot",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "CrewTemplateName",
                table: "Runs");
        }
    }
}
