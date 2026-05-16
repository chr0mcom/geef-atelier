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
    public async Task Migration_KnowledgeDocuments_HasScopeColumn_WithDefaultZero()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        // Insert a document without specifying Scope — should default to 0 (Global)
        // Use raw ADO.NET to avoid EF format-string interpretation of '{}' in SQL
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(@"
            INSERT INTO ""KnowledgeDocuments""
                (""Id"", ""Title"", ""Description"", ""OriginalFilename"", ""ContentType"",
                 ""FileSizeBytes"", ""RawContent"", ""Tags"", ""EmbeddingModel"", ""EmbeddingDimensions"",
                 ""ChunkCount"", ""CreatedAt"", ""UpdatedAt"")
            VALUES
                (gen_random_uuid(), 'Scope Default Test', '', 'test.md', 'text/markdown',
                 10, 'hello', '{}', 'openai/text-embedding-3-small', 1536,
                 0, now(), now())", conn);
        await cmd.ExecuteNonQueryAsync();

        // Verify column exists and Scope defaulted to 0
        var entity = await context.KnowledgeDocuments
            .Where(d => d.Title == "Scope Default Test")
            .FirstAsync();
        Assert.Equal(0, entity.Scope);
    }

    [Fact]
    public async Task Migration_KnowledgeDocuments_HasRunIdColumn_Nullable()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        // Insert with explicit RunId = NULL using raw ADO.NET
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(@"
            INSERT INTO ""KnowledgeDocuments""
                (""Id"", ""Title"", ""Description"", ""OriginalFilename"", ""ContentType"",
                 ""FileSizeBytes"", ""RawContent"", ""Tags"", ""EmbeddingModel"", ""EmbeddingDimensions"",
                 ""ChunkCount"", ""CreatedAt"", ""UpdatedAt"", ""RunId"")
            VALUES
                (gen_random_uuid(), 'Nullable RunId Test', '', 'test.md', 'text/markdown',
                 10, 'hello', '{}', 'openai/text-embedding-3-small', 1536,
                 0, now(), now(), NULL)", conn);
        await cmd.ExecuteNonQueryAsync();

        var entity = await context.KnowledgeDocuments
            .Where(d => d.Title == "Nullable RunId Test")
            .FirstAsync();

        Assert.Null(entity.RunId);
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

    [Fact]
    public async Task Migration_GroundingProviderProfiles_ContainsRunAttachmentsEntry()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var profile = await context.GroundingProviderProfiles
            .FirstOrDefaultAsync(p => p.Name == "run-attachments");

        Assert.NotNull(profile);
        Assert.Equal("Run Attachments", profile!.DisplayName);
        Assert.Equal("vector-store", profile.ProviderType);
        Assert.True(profile.IsSystem);
        Assert.True(profile.ProviderSettings.ContainsKey("Scope"));
        Assert.Equal("run-local", profile.ProviderSettings["Scope"]);
    }
}
