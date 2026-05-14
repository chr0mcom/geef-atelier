using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that deleting a Run cascades to its RunLocal KnowledgeDocuments
/// and that document deletion cascades to chunks.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RunDeleteCascadesAttachmentsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private Geef.Atelier.Infrastructure.Persistence.AtelierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<Geef.Atelier.Infrastructure.Persistence.AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new Geef.Atelier.Infrastructure.Persistence.AtelierDbContext(options);
    }

    [Fact]
    public async Task DeletingRun_CascadesDeleteToRunLocalDocuments()
    {
        await using var setupContext = NewContext();
        await setupContext.Database.MigrateAsync();

        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            BriefingText = "cascade test",
            ConfigJson = "{}",
        };
        setupContext.Runs.Add(run);
        await setupContext.SaveChangesAsync();

        // Create a RunLocal document attached to this run
        var docRepo = new KnowledgeDocumentRepository(NewContext());
        var doc = BuildDocument(scope: KnowledgeScope.RunLocal, runId: run.Id);
        await docRepo.CreateAsync(doc, CancellationToken.None);

        // Verify document exists
        var before = await NewContext().KnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == doc.Id);
        Assert.NotNull(before);

        // Delete the run — should cascade-delete the document
        await using var deleteContext = NewContext();
        await deleteContext.Runs.Where(r => r.Id == run.Id).ExecuteDeleteAsync();

        // Document should be gone
        var after = await NewContext().KnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == doc.Id);
        Assert.Null(after);
    }

    [Fact]
    public async Task DeletingRun_CascadesDeleteToChunks()
    {
        await using var setupContext = NewContext();
        await setupContext.Database.MigrateAsync();

        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            BriefingText = "chunk cascade test",
            ConfigJson = "{}",
        };
        setupContext.Runs.Add(run);
        await setupContext.SaveChangesAsync();

        // Create a RunLocal document
        var docRepo = new KnowledgeDocumentRepository(NewContext());
        var doc = BuildDocument(scope: KnowledgeScope.RunLocal, runId: run.Id);
        await docRepo.CreateAsync(doc, CancellationToken.None);

        // Insert a chunk for that document via ADO.NET (matching VectorSearchRepository pattern)
        var connectionString = _postgres.GetConnectionString();
        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var chunkId = Guid.NewGuid();
        var vectorLiteral = "[" + string.Join(",", new float[1536].Select((_, i) => i == 0 ? "1.0" : "0.0")) + "]";
        await using var cmd = new Npgsql.NpgsqlCommand(@"
            INSERT INTO ""KnowledgeDocumentChunks""
                (""Id"", ""DocumentId"", ""ChunkIndex"", ""Content"", ""Embedding"", ""TokenCount"", ""CreatedAt"")
            VALUES (@id, @docId, 0, 'test chunk', @vec::vector, 5, now())", conn);
        cmd.Parameters.AddWithValue("@id", chunkId);
        cmd.Parameters.AddWithValue("@docId", doc.Id);
        cmd.Parameters.AddWithValue("@vec", vectorLiteral);
        await cmd.ExecuteNonQueryAsync();

        // Verify chunk exists using raw ADO.NET (EF cannot read the vector column type)
        async Task<bool> ChunkExistsAsync()
        {
            await using var checkConn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
            await checkConn.OpenAsync();
            await using var checkCmd = new Npgsql.NpgsqlCommand(
                @"SELECT COUNT(*) FROM ""KnowledgeDocumentChunks"" WHERE ""Id"" = @id", checkConn);
            checkCmd.Parameters.AddWithValue("@id", chunkId);
            var result = await checkCmd.ExecuteScalarAsync();
            return Convert.ToInt64(result) > 0;
        }

        Assert.True(await ChunkExistsAsync(), "chunk should exist before cascade delete");

        // Delete run — cascades to document — cascades to chunk
        await using var deleteContext = NewContext();
        await deleteContext.Runs.Where(r => r.Id == run.Id).ExecuteDeleteAsync();

        Assert.False(await ChunkExistsAsync(), "chunk should be deleted by cascade");
    }

    [Fact]
    public async Task DeletingRun_DoesNotAffectGlobalDocuments()
    {
        await using var setupContext = NewContext();
        await setupContext.Database.MigrateAsync();

        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            BriefingText = "isolation test",
            ConfigJson = "{}",
        };
        setupContext.Runs.Add(run);
        await setupContext.SaveChangesAsync();

        // Create a Global document (no RunId)
        var docRepo = new KnowledgeDocumentRepository(NewContext());
        var globalDoc = BuildDocument(scope: KnowledgeScope.Global, runId: null);
        await docRepo.CreateAsync(globalDoc, CancellationToken.None);

        // Delete the run
        await using var deleteContext = NewContext();
        await deleteContext.Runs.Where(r => r.Id == run.Id).ExecuteDeleteAsync();

        // Global document must still exist
        var globalAfter = await NewContext().KnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == globalDoc.Id);
        Assert.NotNull(globalAfter);
    }

    private static KnowledgeDocument BuildDocument(KnowledgeScope scope, Guid? runId)
    {
        var now = DateTimeOffset.UtcNow;
        return new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: "Cascade Test Doc",
            Description: "",
            OriginalFilename: "test.md",
            ContentType: "text/markdown",
            FileSizeBytes: 10,
            RawContent: "content",
            Tags: [],
            EmbeddingModel: "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now,
            Scope: scope,
            RunId: runId);
    }
}
