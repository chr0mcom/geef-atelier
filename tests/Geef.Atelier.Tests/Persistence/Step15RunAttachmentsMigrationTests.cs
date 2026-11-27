using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that the Step15RunAttachments migration applies cleanly and produces the expected schema.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step15RunAttachmentsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AtelierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AtelierDbContext(options);
    }

    [Fact]
    public async Task Migration_AppliesCleanly_NoPendingMigrationsAfter()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migration_FkConstraint_RejectsInvalidRunId()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var fakeRunId = Guid.NewGuid();

        // Inserting a document with a non-existent RunId should violate the FK constraint
        await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(@"
                INSERT INTO ""KnowledgeDocuments""
                    (""Id"", ""Title"", ""Description"", ""OriginalFilename"", ""ContentType"",
                     ""FileSizeBytes"", ""RawContent"", ""Tags"", ""EmbeddingModel"", ""EmbeddingDimensions"",
                     ""ChunkCount"", ""CreatedAt"", ""UpdatedAt"", ""RunId"")
                VALUES
                    (gen_random_uuid(), 'FK Test', '', 'test.md', 'text/markdown',
                     10, 'hello', '{}', 'openai/text-embedding-3-small', 1536,
                     0, now(), now(), @runId)", conn);
            cmd.Parameters.AddWithValue("@runId", fakeRunId);
            await cmd.ExecuteNonQueryAsync();
        });
    }

}
