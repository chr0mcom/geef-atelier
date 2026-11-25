using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Research (stage 4). Additive and idempotent; the statement can run again on a database where it already ran:
    /// <c>Orders</c> gains <c>ResearchJson</c> (the research of the order as JSON, set once by its first run) and <c>ResearchAt</c>
    /// (when it was set; counts toward the daily limit). No index, no data migration: existing orders keep <c>NULL</c>.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261128120000_Step51Research")]
    public partial class Step51Research : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "ResearchJson" text                     NULL,
    ADD COLUMN IF NOT EXISTS "ResearchAt"   timestamp with time zone NULL;
""");
        }

        /// <summary>
        /// Drops the two columns; for tests only. In production the way back is the previous image: the columns are nullable and the
        /// code of stage 3 does not know them.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Orders"
    DROP COLUMN IF EXISTS "ResearchAt",
    DROP COLUMN IF EXISTS "ResearchJson";
""");
        }
    }
}
