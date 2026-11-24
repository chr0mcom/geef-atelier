using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Intake before the first run (Etappe 2). Additive and idempotent; every statement can run again on a database where
    /// it already ran, partly or fully:
    /// <list type="number">
    ///   <item>Six intake columns on <c>Orders</c> (outcome, attempts and four timestamps) and two partial indexes for the
    ///   intake worker (orders being analysed, orders waiting for answers).</item>
    ///   <item><c>UserProfiles</c>: what a person tells about themselves once, one row per user name, no foreign key.</item>
    ///   <item><c>OrderIntakeShadows</c>: shadow-mode records, deleted together with their order.</item>
    /// </list>
    /// No data migration: existing orders keep <c>IntakeOutcome = NULL</c> (not clarified).
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261126120000_Step49Intake")]
    public partial class Step49Intake : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "IntakeOutcome"    character varying(20)    NULL,
    ADD COLUMN IF NOT EXISTS "IntakeAttempts"   integer                  NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "IntakeStartedAt"  timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "IntakeFinishedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "QuestionsAskedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "AnsweredAt"       timestamp with time zone NULL;
CREATE INDEX IF NOT EXISTS "IX_Orders_Intake_Analyzing"  ON "Orders" ("CreatedAt")        WHERE "State" = 'Analyzing';
CREATE INDEX IF NOT EXISTS "IX_Orders_Intake_NeedsInput" ON "Orders" ("QuestionsAskedAt") WHERE "State" = 'NeedsInput';
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "UserProfiles" (
    "Username"     text                     NOT NULL,
    "DisplayName"  character varying(200)   NULL,
    "Role"         character varying(200)   NULL,
    "Organization" character varying(200)   NULL,
    "Formality"    character varying(20)    NOT NULL DEFAULT 'Unset',
    "Language"     character varying(10)    NULL,
    "Signature"    character varying(1000)  NULL,
    "NoGos"        character varying(2000)  NULL,
    "FreeText"     character varying(4000)  NULL,
    "CreatedAt"    timestamp with time zone NOT NULL,
    "UpdatedAt"    timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UserProfiles" PRIMARY KEY ("Username")
);
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "OrderIntakeShadows" (
    "OrderId"       uuid                     NOT NULL,
    "Status"        character varying(20)    NOT NULL,
    "Attempts"      integer                  NOT NULL DEFAULT 0,
    "ClaimedAt"     timestamp with time zone NULL,
    "DurationMs"    integer                  NULL,
    "QuestionCount" integer                  NULL,
    "RecordJson"    jsonb                    NULL,
    "CreatedAt"     timestamp with time zone NOT NULL,
    "CompletedAt"   timestamp with time zone NULL,
    CONSTRAINT "PK_OrderIntakeShadows" PRIMARY KEY ("OrderId")
);
CREATE INDEX IF NOT EXISTS "IX_OrderIntakeShadows_Pending" ON "OrderIntakeShadows" ("CreatedAt") WHERE "Status" = 'Pending';
""");

            migrationBuilder.Sql("""
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'FK_OrderIntakeShadows_Orders_OrderId' AND conrelid = '"OrderIntakeShadows"'::regclass) THEN
        ALTER TABLE "OrderIntakeShadows"
            ADD CONSTRAINT "FK_OrderIntakeShadows_Orders_OrderId"
            FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE CASCADE;
    END IF;
END $$;
""");
        }

        /// <summary>
        /// Drops the additive schema. Profiles are lost, so in production the way back is the previous image, not this
        /// method; orders still in <c>Analyzing</c>/<c>NeedsInput</c> must be started or stopped first.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TABLE IF EXISTS "OrderIntakeShadows", "UserProfiles";
DROP INDEX IF EXISTS "IX_Orders_Intake_Analyzing";
DROP INDEX IF EXISTS "IX_Orders_Intake_NeedsInput";
ALTER TABLE "Orders"
    DROP COLUMN IF EXISTS "AnsweredAt",
    DROP COLUMN IF EXISTS "QuestionsAskedAt",
    DROP COLUMN IF EXISTS "IntakeFinishedAt",
    DROP COLUMN IF EXISTS "IntakeStartedAt",
    DROP COLUMN IF EXISTS "IntakeAttempts",
    DROP COLUMN IF EXISTS "IntakeOutcome";
""");
        }
    }
}
