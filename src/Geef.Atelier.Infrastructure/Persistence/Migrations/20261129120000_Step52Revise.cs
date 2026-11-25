using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Changing a delivered text (stage 5). Additive and idempotent; the statement can run again on a database where it already ran:
    /// <c>Runs</c> gains <c>RevisionHint</c> (what the person asked to change for that version). No index, no data migration: existing
    /// runs keep <c>NULL</c>; <c>ParentRunId</c> and <c>SeedDraftText</c> exist already.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261129120000_Step52Revise")]
    public partial class Step52Revise : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Runs"
    ADD COLUMN IF NOT EXISTS "RevisionHint" text NULL;
""");
        }

        /// <summary>
        /// Drops the column; for tests only. In production the way back is the previous image: the column is nullable and the code of
        /// the stage before does not know it.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Runs"
    DROP COLUMN IF EXISTS "RevisionHint";
""");
        }
    }
}
