using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;

/// <summary>
/// Raw-SQL vector search implementation of <see cref="IVectorSearchRepository"/>.
/// Uses Npgsql ADO.NET directly for ANN distance queries because
/// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql 10.x.
/// Named parameters (@name) are used instead of positional ($N) to avoid
/// Npgsql auto-prepare cache conflicts across pooled connections.
/// </summary>
internal sealed class VectorSearchRepository(AtelierDbContext context) : IVectorSearchRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        IReadOnlyList<string>? tagFilter,
        CancellationToken ct)
    {
        // Format query vector as postgres vector literal: [f1,f2,...]
        var vectorLiteral = "[" + string.Join(",",
            queryEmbedding.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

        var sql = tagFilter is { Count: > 0 }
            ? @"SELECT c.""Id"", c.""DocumentId"", c.""ChunkIndex"", c.""Content"",
                       c.""Embedding""::text AS ""EmbeddingText"", c.""TokenCount"", c.""CreatedAt"",
                       d.""Title"" AS ""DocumentTitle"",
                       1.0 - (c.""Embedding"" <=> @vec::vector) AS ""Similarity""
                FROM ""KnowledgeDocumentChunks"" c
                JOIN ""KnowledgeDocuments"" d ON c.""DocumentId"" = d.""Id""
                WHERE d.""Tags"" && @tags::text[]
                ORDER BY c.""Embedding"" <=> @vec::vector
                LIMIT @topk"
            : @"SELECT c.""Id"", c.""DocumentId"", c.""ChunkIndex"", c.""Content"",
                       c.""Embedding""::text AS ""EmbeddingText"", c.""TokenCount"", c.""CreatedAt"",
                       d.""Title"" AS ""DocumentTitle"",
                       1.0 - (c.""Embedding"" <=> @vec::vector) AS ""Similarity""
                FROM ""KnowledgeDocumentChunks"" c
                JOIN ""KnowledgeDocuments"" d ON c.""DocumentId"" = d.""Id""
                ORDER BY c.""Embedding"" <=> @vec::vector
                LIMIT @topk";

        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@vec", vectorLiteral);
        cmd.Parameters.AddWithValue("@topk", topK);
        if (tagFilter is { Count: > 0 })
            cmd.Parameters.AddWithValue("@tags", tagFilter.ToArray());

        var results = new List<VectorSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var chunkId = reader.GetGuid(reader.GetOrdinal("Id"));
            var documentId = reader.GetGuid(reader.GetOrdinal("DocumentId"));
            var chunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex"));
            var content = reader.GetString(reader.GetOrdinal("Content"));
            var embeddingText = reader.GetString(reader.GetOrdinal("EmbeddingText"));
            var tokenCount = reader.GetInt32(reader.GetOrdinal("TokenCount"));
            var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CreatedAt"));
            var documentTitle = reader.GetString(reader.GetOrdinal("DocumentTitle"));
            var similarity = reader.GetDouble(reader.GetOrdinal("Similarity"));

            var embedding = ParseVector(embeddingText);

            var chunk = new KnowledgeDocumentChunk(
                chunkId, documentId, chunkIndex, content, embedding, tokenCount, createdAt);

            results.Add(new VectorSearchResult(chunk, documentTitle, similarity));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
    {
        // EF ValueConverter outputs a varchar string which Postgres rejects for the vector(1536) column type.
        // Use raw ADO.NET INSERT with a ::vector cast, on a fresh dedicated connection.
        var vectorLiteral = "[" + string.Join(",",
            chunk.Embedding.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

        const string sql = @"
            INSERT INTO ""KnowledgeDocumentChunks""
                (""Id"", ""DocumentId"", ""ChunkIndex"", ""Content"", ""Embedding"", ""TokenCount"", ""CreatedAt"")
            VALUES (@id, @docId, @chunkIdx, @content, @vec::vector, @tokenCount, @createdAt)";

        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", chunk.Id);
        cmd.Parameters.AddWithValue("@docId", chunk.DocumentId);
        cmd.Parameters.AddWithValue("@chunkIdx", chunk.ChunkIndex);
        cmd.Parameters.AddWithValue("@content", chunk.Content);
        cmd.Parameters.AddWithValue("@vec", vectorLiteral);
        cmd.Parameters.AddWithValue("@tokenCount", chunk.TokenCount);
        cmd.Parameters.AddWithValue("@createdAt", chunk.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);

        return chunk;
    }

    /// <inheritdoc/>
    public async Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        await context.KnowledgeDocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Opens a fresh Npgsql connection from the same connection string as the EF context.
    /// This avoids prepared-statement cache conflicts that occur when reusing the EF shared connection
    /// for raw SQL with named parameters.
    /// </summary>
    private Npgsql.NpgsqlConnection NewConnection()
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        return new Npgsql.NpgsqlConnection(connectionString);
    }

    private static float[] ParseVector(string text)
    {
        var trimmed = text.Trim('[', ']');
        if (string.IsNullOrEmpty(trimmed)) return [];
        var parts = trimmed.Split(',');
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            result[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }
}
