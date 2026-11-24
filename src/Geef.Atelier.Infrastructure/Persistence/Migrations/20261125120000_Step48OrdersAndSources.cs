using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Orders and sources (Etappe 1). Additive and idempotent; every statement can run again on a database where it
    /// already ran, partly or fully:
    /// <list type="number">
    ///   <item><c>Orders</c>, <c>Sources</c>, <c>OrderSources</c> and <c>RunSourceUsages</c> with their indexes and
    ///   foreign keys. A file is stored once per owner: <c>UX_Sources_OwnerUsername_Sha256</c> is unique.</item>
    ///   <item><c>Runs.OrderId</c> and <c>Runs.OrderVersion</c> with a cascading foreign key to <c>Orders</c>.</item>
    ///   <item>Data migration: every chain of existing runs (edge = <c>COALESCE(ParentCompositionRunId, ParentRunId)</c>)
    ///   becomes one <c>Careful</c>/<c>Ready</c>/<c>Legacy</c> order whose id is the id of the root run. Learning runs stay
    ///   without an order; a run whose parent is gone becomes a root. Standard runs are numbered 1..n by creation time,
    ///   composition runs carry the version of the task run they lead to. Only runs without an order take part, so a
    ///   second run changes nothing.</item>
    ///   <item>Only after the data migration: a unique partial index so a version of an order is started only once.</item>
    /// </list>
    /// The chain is collected in a temporary table inside one <c>DO</c> block, so the data migration is a single
    /// statement and works whether or not the caller wraps the migration in a transaction.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261125120000_Step48OrdersAndSources")]
    public partial class Step48OrdersAndSources : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "Orders" (
    "Id"               uuid                     NOT NULL,
    "CreatedByUser"    text                     NULL,
    "RequestText"      text                     NOT NULL,
    "Mode"             character varying(20)    NOT NULL,
    "State"            character varying(20)    NOT NULL,
    "Entry"            character varying(20)    NOT NULL,
    "CrewTemplateName" character varying(200)   NULL,
    "ClarifyJson"      jsonb                    NULL,
    "AnswersJson"      jsonb                    NULL,
    "ClaimedAt"        timestamp with time zone NULL,
    "CreatedAt"        timestamp with time zone NOT NULL,
    "UpdatedAt"        timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_Orders_CreatedByUser_CreatedAt" ON "Orders" ("CreatedByUser", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_Orders_State" ON "Orders" ("State");
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "Sources" (
    "Id"            uuid                     NOT NULL,
    "OwnerUsername" text                     NOT NULL,
    "FileName"      character varying(255)   NOT NULL,
    "ContentType"   character varying(100)   NOT NULL,
    "Kind"          character varying(20)    NOT NULL,
    "SizeBytes"     bigint                   NOT NULL,
    "Sha256"        character(64)            NOT NULL,
    "StoragePath"   character varying(500)   NULL,
    "ExtractedText" text                     NULL,
    "PageCount"     integer                  NULL,
    "CharCount"     integer                  NOT NULL,
    "WordCount"     integer                  NOT NULL,
    "Quality"       character varying(20)    NOT NULL,
    "WarningsJson"  jsonb                    NOT NULL DEFAULT '[]'::jsonb,
    "Origin"        character varying(20)    NOT NULL,
    "InLibrary"     boolean                  NOT NULL DEFAULT false,
    "AlwaysInclude" boolean                  NOT NULL DEFAULT false,
    "ExpiresAt"     timestamp with time zone NULL,
    "CreatedAt"     timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Sources" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_Sources_OwnerUsername_CreatedAt" ON "Sources" ("OwnerUsername", "CreatedAt" DESC);
DROP INDEX IF EXISTS "IX_Sources_OwnerUsername_Sha256";
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Sources_OwnerUsername_Sha256" ON "Sources" ("OwnerUsername", "Sha256");
CREATE INDEX IF NOT EXISTS "IX_Sources_ExpiresAt" ON "Sources" ("ExpiresAt") WHERE "ExpiresAt" IS NOT NULL;
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "OrderSources" (
    "OrderId"       uuid                     NOT NULL,
    "SourceId"      uuid                     NOT NULL,
    "Role"          character varying(30)    NOT NULL,
    "Position"      integer                  NOT NULL,
    "PageSelection" character varying(200)   NULL,
    "AddedAt"       timestamp with time zone NOT NULL,
    CONSTRAINT "PK_OrderSources" PRIMARY KEY ("OrderId", "SourceId")
);
CREATE INDEX IF NOT EXISTS "IX_OrderSources_SourceId" ON "OrderSources" ("SourceId");
""");

            migrationBuilder.Sql(ForeignKey("OrderSources", "FK_OrderSources_Orders_OrderId", "OrderId", "Orders", "CASCADE"));
            migrationBuilder.Sql(ForeignKey("OrderSources", "FK_OrderSources_Sources_SourceId", "SourceId", "Sources", "RESTRICT"));

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "RunSourceUsages" (
    "Id"            uuid                     NOT NULL,
    "RunId"         uuid                     NOT NULL,
    "SourceId"      uuid                     NULL,
    "FileName"      character varying(255)   NOT NULL,
    "Mode"          character varying(20)    NOT NULL,
    "Target"        character varying(20)    NOT NULL,
    "CharsIncluded" integer                  NOT NULL,
    "PagesIncluded" character varying(200)   NULL,
    "Position"      integer                  NOT NULL,
    "CreatedAt"     timestamp with time zone NOT NULL,
    CONSTRAINT "PK_RunSourceUsages" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_RunSourceUsages_RunId" ON "RunSourceUsages" ("RunId");
""");

            migrationBuilder.Sql(ForeignKey("RunSourceUsages", "FK_RunSourceUsages_Runs_RunId", "RunId", "Runs", "CASCADE"));
            migrationBuilder.Sql(ForeignKey("RunSourceUsages", "FK_RunSourceUsages_Sources_SourceId", "SourceId", "Sources", "SET NULL"));

            migrationBuilder.Sql("""
ALTER TABLE "Runs"
    ADD COLUMN IF NOT EXISTS "OrderId"      uuid    NULL,
    ADD COLUMN IF NOT EXISTS "OrderVersion" integer NULL;
CREATE INDEX IF NOT EXISTS "IX_Runs_OrderId_OrderVersion" ON "Runs" ("OrderId", "OrderVersion");
""");

            migrationBuilder.Sql(ForeignKey("Runs", "FK_Runs_Orders_OrderId", "OrderId", "Orders", "CASCADE"));

            migrationBuilder.Sql(DataMigrationSql);

            migrationBuilder.Sql("""
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Runs_OrderId_OrderVersion_Standard"
    ON "Runs" ("OrderId", "OrderVersion") WHERE "OrderId" IS NOT NULL AND "Kind" = 0;
""");
        }

        /// <summary>
        /// Drops the additive schema. Runs keep everything else; files under the sources volume stay orphaned, so back
        /// the volume up before running this in production.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP INDEX IF EXISTS "UX_Runs_OrderId_OrderVersion_Standard";
ALTER TABLE "Runs" DROP CONSTRAINT IF EXISTS "FK_Runs_Orders_OrderId";
DROP INDEX IF EXISTS "IX_Runs_OrderId_OrderVersion";
ALTER TABLE "Runs"
    DROP COLUMN IF EXISTS "OrderVersion",
    DROP COLUMN IF EXISTS "OrderId";
DROP TABLE IF EXISTS "RunSourceUsages", "OrderSources", "Sources", "Orders";
""");
        }

        internal const string DataMigrationSql = """
DO $$
BEGIN
    DROP TABLE IF EXISTS step48_chain;
    CREATE TEMP TABLE step48_chain ("RunId" uuid PRIMARY KEY, "RootId" uuid NOT NULL);

    INSERT INTO step48_chain ("RunId", "RootId")
    WITH RECURSIVE chain AS (
        SELECT r."Id" AS "RunId", r."Id" AS "RootId", 0 AS depth
        FROM "Runs" r
        WHERE r."Kind" <> 1 AND r."OrderId" IS NULL
          AND NOT EXISTS (SELECT 1 FROM "Runs" p
                          WHERE p."Id" = COALESCE(r."ParentCompositionRunId", r."ParentRunId") AND p."Kind" <> 1)
        UNION ALL
        SELECT c."Id", chain."RootId", chain.depth + 1
        FROM "Runs" c
        JOIN chain ON COALESCE(c."ParentCompositionRunId", c."ParentRunId") = chain."RunId"
        WHERE c."Kind" <> 1 AND c."OrderId" IS NULL AND chain.depth < 100
    )
    SELECT "RunId", "RootId" FROM chain
    ON CONFLICT ("RunId") DO NOTHING;

    INSERT INTO "Orders" ("Id", "CreatedByUser", "RequestText", "Mode", "State", "Entry", "CrewTemplateName", "CreatedAt", "UpdatedAt")
    SELECT root."Id", root."CreatedByUser",
           CASE WHEN root."Kind" = 2
                THEN COALESCE(NULLIF(root."ConfigJson"->>'original_task', ''),
                              regexp_replace(root."BriefingText", '^Compose a crew for the following task:\s*', ''))
                ELSE root."BriefingText" END,
           'Careful', 'Ready', 'Legacy', NULL, root."CreatedAt",
           COALESCE((SELECT max(COALESCE(x."CompletedAt", x."CreatedAt"))
                     FROM step48_chain c2 JOIN "Runs" x ON x."Id" = c2."RunId"
                     WHERE c2."RootId" = root."Id"), root."CreatedAt")
    FROM "Runs" root
    WHERE root."Id" IN (SELECT DISTINCT "RootId" FROM step48_chain)
    ON CONFLICT ("Id") DO NOTHING;

    UPDATE "Runs" r
    SET "OrderId" = v."RootId", "OrderVersion" = v."Version"
    FROM (
        SELECT c."RunId", c."RootId",
               CASE WHEN x."Kind" = 0
                    THEN ROW_NUMBER() OVER (PARTITION BY c."RootId", (x."Kind" = 0) ORDER BY x."CreatedAt", x."Id")
                    ELSE 1 + (SELECT count(*) FROM step48_chain c3 JOIN "Runs" y ON y."Id" = c3."RunId"
                              WHERE c3."RootId" = c."RootId" AND y."Kind" = 0 AND y."CreatedAt" < x."CreatedAt")
               END AS "Version"
        FROM step48_chain c JOIN "Runs" x ON x."Id" = c."RunId"
    ) v
    WHERE r."Id" = v."RunId" AND r."OrderId" IS NULL;

    DROP TABLE IF EXISTS step48_chain;
END $$;
""";

        private static string ForeignKey(string table, string name, string column, string principal, string onDelete) => $"""
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = '{name}' AND conrelid = '"{table}"'::regclass) THEN
        ALTER TABLE "{table}"
            ADD CONSTRAINT "{name}"
            FOREIGN KEY ("{column}") REFERENCES "{principal}"("Id") ON DELETE {onDelete};
    END IF;
END $$;
""";
    }
}
