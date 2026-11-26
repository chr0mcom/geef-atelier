using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Step53 against real Postgres: a Step52 database with a source gains <c>UploadTickets</c> and <c>SourceRefusals</c> with their
/// indexes and the foreign key, the source stays as it was, the statements run again without change and keep stored rows, deleting a
/// source empties <c>SourceId</c>, and the way back removes both tables.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step53UploadsMigrationTests : IAsyncLifetime
{
    private const string Step52 = "20261129120000_Step52Revise";
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _source = Guid.NewGuid();
    private readonly Guid _ticket = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public void The_migration_carries_both_attributes()
    {
        var type = typeof(Step53Uploads);
        Assert.NotNull(type.GetCustomAttributes(typeof(DbContextAttribute), false).SingleOrDefault());
        var migration = Assert.IsType<MigrationAttribute>(type.GetCustomAttributes(typeof(MigrationAttribute), false).Single());
        Assert.Equal("20261130120000_Step53Uploads", migration.Id);
    }

    [Fact]
    public async Task A_step52_database_gains_both_tables_and_keeps_its_source()
    {
        await MigrateToStep52WithASourceAsync();
        var before = await SourceJsonAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertSchemaAsync(check);
        Assert.Equal(before, await SourceJsonAsync());
        Assert.Equal(0, await check.UploadTickets.CountAsync());
        Assert.Equal(0, await check.SourceRefusals.CountAsync());
    }

    [Fact]
    public async Task Running_up_twice_changes_nothing_and_keeps_stored_rows()
    {
        await MigrateToStep52WithASourceAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await SeedTicketAndRefusalAsync(db);
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertSchemaAsync(db);
        }

        await using var read = NewContext();
        var ticket = await read.UploadTickets.AsNoTracking().SingleAsync();
        Assert.Equal((_ticket, _source, TokenHash), (ticket.Id, ticket.SourceId, ticket.TokenHash));
        Assert.Equal(1, await read.SourceRefusals.CountAsync());
    }

    [Fact]
    public async Task Deleting_a_source_empties_the_source_of_its_ticket()
    {
        await MigrateToStep52WithASourceAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedTicketAndRefusalAsync(db);

        await db.Sources.Where(s => s.Id == _source).ExecuteDeleteAsync();

        var ticket = await db.UploadTickets.AsNoTracking().SingleAsync();
        Assert.Null(ticket.SourceId);
    }

    [Fact]
    public async Task The_token_hash_is_unique()
    {
        await MigrateToStep52WithASourceAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedTicketAndRefusalAsync(db);

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UploadTickets" ("Id", "OwnerUsername", "FileName", "TokenHash", "State", "CreatedAt", "TokenExpiresAt", "ExpiresAt")
            VALUES ({Guid.NewGuid()}, 'user-b', 'b.txt', {TokenHash}, 'Open', {T0}, {T0}, {T0})
            """));
    }

    [Fact]
    public async Task A_ticket_table_from_before_the_last_refusal_column_gains_it_and_keeps_its_rows()
    {
        await MigrateToStep52WithASourceAsync();
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261130120000_Step53Uploads");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "UploadTickets" DROP COLUMN "LastRefusalCode";""");
            await SeedTicketAndRefusalAsync(db);
            await RunUpAsync(db);
            await AssertSchemaAsync(db);
        }

        await using var read = NewContext();
        var ticket = await read.UploadTickets.AsNoTracking().SingleAsync();
        Assert.Equal((_ticket, (string?)null), (ticket.Id, ticket.LastRefusalCode));
    }

    [Fact]
    public async Task Down_removes_both_tables_and_up_restores_them()
    {
        await MigrateToStep52WithASourceAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var raw = NewContext();
        await RunDownAsync(raw);
        await RunDownAsync(raw);
        Assert.Equal(0, await TableCountAsync(raw));
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Sources" """).SingleAsync());

        await RunUpAsync(raw);
        await AssertSchemaAsync(raw);
    }

    private async Task MigrateToStep52WithASourceAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step52);

        await using var seed = NewContext();
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Sources" ("Id", "OwnerUsername", "FileName", "ContentType", "Kind", "SizeBytes", "Sha256", "CharCount", "WordCount",
                                   "Quality", "Origin", "CreatedAt")
            VALUES ({_source}, 'user-a', 'notes.txt', 'text/plain', 'Text', 12, {Sha}, 12, 2, 'Good', 'McpAttachment', {T0})
            """);
    }

    private async Task SeedTicketAndRefusalAsync(AtelierDbContext db)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UploadTickets" ("Id", "OwnerUsername", "FileName", "DeclaredBytes", "TokenHash", "State", "Attempts", "SourceId",
                                         "CreatedAt", "TokenExpiresAt", "ExpiresAt", "UsedAt")
            VALUES ({_ticket}, 'user-a', 'notes.txt', 12, {TokenHash}, 'Used', 1, {_source}, {T0}, {T0.AddMinutes(15)}, {T0.AddHours(24)}, {T0})
            """);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "SourceRefusals" ("Id", "CreatedAt", "OwnerUsername", "Path", "Code", "Kind", "Extension", "DeclaredType", "SizeBytes")
            VALUES ({Guid.NewGuid()}, {T0}, 'user-a', 'mcp_ticket', 'pdf_no_text', 'Pdf', 'pdf', 'application/pdf', 2048)
            """);
    }

    private async Task<string> SourceJsonAsync()
    {
        await using var db = NewContext();
        return await db.Database.SqlQuery<string>($"""SELECT row_to_json(s)::text AS "Value" FROM "Sources" s WHERE s."Id" = {_source}""").SingleAsync();
    }

    private static async Task AssertSchemaAsync(AtelierDbContext db)
    {
        Assert.Equal(2, await TableCountAsync(db));

        var columns = await db.Database.SqlQueryRaw<ColumnRow>("""
            SELECT table_name AS "Table", column_name AS "Name", data_type AS "Type", is_nullable AS "Nullable",
                   COALESCE(character_maximum_length, 0) AS "Length"
            FROM information_schema.columns
            WHERE table_name IN ('UploadTickets', 'SourceRefusals')
            """).ToListAsync();
        var expected = new[]
        {
            new ColumnRow("UploadTickets", "Id", "uuid", "NO", 0),
            new ColumnRow("UploadTickets", "OwnerUsername", "text", "NO", 0),
            new ColumnRow("UploadTickets", "FileName", "character varying", "NO", 255),
            new ColumnRow("UploadTickets", "DeclaredBytes", "bigint", "YES", 0),
            new ColumnRow("UploadTickets", "TokenHash", "character", "NO", 64),
            new ColumnRow("UploadTickets", "State", "character varying", "NO", 16),
            new ColumnRow("UploadTickets", "Attempts", "integer", "NO", 0),
            new ColumnRow("UploadTickets", "SourceId", "uuid", "YES", 0),
            new ColumnRow("UploadTickets", "CreatedAt", "timestamp with time zone", "NO", 0),
            new ColumnRow("UploadTickets", "TokenExpiresAt", "timestamp with time zone", "NO", 0),
            new ColumnRow("UploadTickets", "ExpiresAt", "timestamp with time zone", "NO", 0),
            new ColumnRow("UploadTickets", "ReceivingSince", "timestamp with time zone", "YES", 0),
            new ColumnRow("UploadTickets", "UsedAt", "timestamp with time zone", "YES", 0),
            new ColumnRow("UploadTickets", "LastRefusalCode", "character varying", "YES", 40),
            new ColumnRow("SourceRefusals", "Id", "uuid", "NO", 0),
            new ColumnRow("SourceRefusals", "CreatedAt", "timestamp with time zone", "NO", 0),
            new ColumnRow("SourceRefusals", "OwnerUsername", "text", "NO", 0),
            new ColumnRow("SourceRefusals", "Path", "character varying", "NO", 16),
            new ColumnRow("SourceRefusals", "Code", "character varying", "NO", 40),
            new ColumnRow("SourceRefusals", "Kind", "character varying", "YES", 16),
            new ColumnRow("SourceRefusals", "Extension", "character varying", "YES", 8),
            new ColumnRow("SourceRefusals", "DeclaredType", "character varying", "YES", 100),
            new ColumnRow("SourceRefusals", "SizeBytes", "bigint", "YES", 0),
        };
        Assert.Equal(expected.OrderBy(c => c.Table).ThenBy(c => c.Name), columns.OrderBy(c => c.Table).ThenBy(c => c.Name));

        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename IN ('UploadTickets', 'SourceRefusals') ORDER BY indexname
            """).ToListAsync();
        Assert.Contains(indexes, i => i.Contains("UNIQUE INDEX \"UX_UploadTickets_TokenHash\"", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.Contains("\"IX_UploadTickets_OwnerUsername_State\"", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.Contains("\"IX_UploadTickets_ExpiresAt\"", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.Contains("\"IX_SourceRefusals_CreatedAt\"", StringComparison.Ordinal));

        var foreignKeys = await db.Database.SqlQueryRaw<string>("""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conname = 'FK_UploadTickets_Sources_SourceId' AND conrelid = '"UploadTickets"'::regclass
            """).ToListAsync();
        Assert.Equal("FOREIGN KEY (\"SourceId\") REFERENCES \"Sources\"(\"Id\") ON DELETE SET NULL", Assert.Single(foreignKeys));
    }

    private static Task<int> TableCountAsync(AtelierDbContext db) =>
        db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_name IN ('UploadTickets', 'SourceRefusals')
            """).SingleAsync();

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step53Uploads().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step53Uploads().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private sealed record ColumnRow(string Table, string Name, string Type, string Nullable, int Length);
}
