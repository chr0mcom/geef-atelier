using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Honest writing (stage 3). Additive and idempotent; the statements can run again on a database where they already ran:
    /// <list type="number">
    ///   <item><c>Findings</c> gains <c>Category</c>, <c>Code</c> and <c>Quote</c> (the passage of the text a finding is about).</item>
    ///   <item><c>Runs</c> gains <c>FactCheckMode</c>, the mode the fact check of a direct-path run used.</item>
    /// </list>
    /// No data migration and no index: existing rows keep <c>NULL</c>, the report JSON becomes schema 2 only for new runs.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261127120000_Step50HonestWriting")]
    public partial class Step50HonestWriting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Findings"
    ADD COLUMN IF NOT EXISTS "Category" character varying(60) NULL,
    ADD COLUMN IF NOT EXISTS "Code"     character varying(60) NULL,
    ADD COLUMN IF NOT EXISTS "Quote"    text                  NULL;
ALTER TABLE "Runs"
    ADD COLUMN IF NOT EXISTS "FactCheckMode" character varying(20) NULL;
""");
        }

        /// <summary>
        /// Drops the four columns. In production the way back is the previous image, not this method: the columns are nullable
        /// and the code of stage 2 reads and writes around them.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Findings"
    DROP COLUMN IF EXISTS "Quote",
    DROP COLUMN IF EXISTS "Code",
    DROP COLUMN IF EXISTS "Category";
ALTER TABLE "Runs"
    DROP COLUMN IF EXISTS "FactCheckMode";
""");
        }
    }
}
