using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Upload tickets and counted refusals (stage 6). Additive and idempotent; every statement can run again on a database where it
    /// already ran, partly or fully:
    /// <list type="number">
    ///   <item><c>UploadTickets</c>: one ticket per file a person's assistant prepares. The token is stored only as its SHA-256 hex
    ///   (<c>UX_UploadTickets_TokenHash</c> is unique); <c>SourceId</c> becomes <c>NULL</c> when the source is deleted;
    ///   <c>LastRefusalCode</c> holds the code of the last checked and refused file, never a name or content.</item>
    ///   <item><c>SourceRefusals</c>: one row per refused file with way, code, kind, extension, declared type and size; never a file
    ///   name or content.</item>
    /// </list>
    /// <c>Sources</c> is not changed, so the code of the stage before reads it as before.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261130120000_Step53Uploads")]
    public partial class Step53Uploads : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "UploadTickets" (
    "Id"             uuid                     NOT NULL,
    "OwnerUsername"  text                     NOT NULL,
    "FileName"       character varying(255)   NOT NULL,
    "DeclaredBytes"  bigint                   NULL,
    "TokenHash"      character(64)            NOT NULL,
    "State"          character varying(16)    NOT NULL,
    "Attempts"       integer                  NOT NULL DEFAULT 0,
    "SourceId"       uuid                     NULL,
    "CreatedAt"      timestamp with time zone NOT NULL,
    "TokenExpiresAt" timestamp with time zone NOT NULL,
    "ExpiresAt"      timestamp with time zone NOT NULL,
    "ReceivingSince" timestamp with time zone NULL,
    "UsedAt"         timestamp with time zone NULL,
    "LastRefusalCode" character varying(40)  NULL,
    CONSTRAINT "PK_UploadTickets" PRIMARY KEY ("Id")
);
ALTER TABLE "UploadTickets" ADD COLUMN IF NOT EXISTS "LastRefusalCode" character varying(40) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_UploadTickets_TokenHash" ON "UploadTickets" ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_UploadTickets_OwnerUsername_State" ON "UploadTickets" ("OwnerUsername", "State");
CREATE INDEX IF NOT EXISTS "IX_UploadTickets_ExpiresAt" ON "UploadTickets" ("ExpiresAt");
""");

            migrationBuilder.Sql("""
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'FK_UploadTickets_Sources_SourceId' AND conrelid = '"UploadTickets"'::regclass) THEN
        ALTER TABLE "UploadTickets"
            ADD CONSTRAINT "FK_UploadTickets_Sources_SourceId"
            FOREIGN KEY ("SourceId") REFERENCES "Sources"("Id") ON DELETE SET NULL;
    END IF;
END $$;
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "SourceRefusals" (
    "Id"            uuid                     NOT NULL,
    "CreatedAt"     timestamp with time zone NOT NULL,
    "OwnerUsername" text                     NOT NULL,
    "Path"          character varying(16)    NOT NULL,
    "Code"          character varying(40)    NOT NULL,
    "Kind"          character varying(16)    NULL,
    "Extension"     character varying(8)     NULL,
    "DeclaredType"  character varying(100)   NULL,
    "SizeBytes"     bigint                   NULL,
    CONSTRAINT "PK_SourceRefusals" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_SourceRefusals_CreatedAt" ON "SourceRefusals" ("CreatedAt");
""");
        }

        /// <summary>
        /// Drops both tables; for tests only. In production the way back is the previous image: it does not know the tables and reads
        /// <c>Sources</c> unchanged.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TABLE IF EXISTS "UploadTickets", "SourceRefusals";
""");
        }
    }
}
