using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Step09AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUser",
                table: "Runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUser",
                table: "Runs");
        }
    }
}
