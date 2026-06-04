using Geef.Atelier.Application.Composition;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>
/// EF-based upsert and raw-SQL cosine search for <see cref="CrewTemplateEmbedding"/> records.
/// Raw SQL is used for the vector(1536) embedding column because
/// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql 10.x.
/// </summary>
internal sealed class CrewTemplateEmbeddingRepository(AtelierDbContext context)
    : ICrewTemplateEmbeddingRepository
{
    /// <inheritdoc/>
    public async Task UpsertAsync(CrewTemplateEmbedding embedding, CancellationToken ct = default)
    {
        var vectorLiteral = ToVectorLiteral(embedding.Embedding);

        // Use raw SQL for upsert so that the vector literal is cast correctly by Postgres.
        const string sql = @"
            INSERT INTO ""CrewTemplateEmbeddings"" (""Id"", ""TemplateName"", ""Domain"", ""Summary"", ""Embedding"", ""CreatedAt"")
            VALUES (@id, @name, @domain, @summary, @vec::vector, @createdAt)
            ON CONFLICT (""TemplateName"") DO UPDATE
            SET ""Domain""    = EXCLUDED.""Domain"",
                ""Summary""   = EXCLUDED.""Summary"",
                ""Embedding"" = EXCLUDED.""Embedding"",
                ""CreatedAt"" = EXCLUDED.""CreatedAt""";

        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id",        embedding.Id);
        cmd.Parameters.AddWithValue("@name",      embedding.TemplateName);
        cmd.Parameters.AddWithValue("@domain",    embedding.Domain);
        cmd.Parameters.AddWithValue("@summary",   embedding.Summary);
        cmd.Parameters.AddWithValue("@vec",       vectorLiteral);
        cmd.Parameters.AddWithValue("@createdAt", embedding.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>> SearchAsync(
        float[] queryEmbedding,
        string? domainHint,
        double sameDomainBoost,
        int topK,
        CancellationToken ct = default)
    {
        var vectorLiteral = ToVectorLiteral(queryEmbedding);

        // Fetch topK*3 raw candidates, then re-rank with domain boost in application code.
        const string sql = @"
            SELECT ""Id"", ""TemplateName"", ""Domain"", ""Summary"", ""CreatedAt"",
                   1.0 - (""Embedding"" <=> @vec::vector) AS ""Similarity""
            FROM ""CrewTemplateEmbeddings""
            WHERE ""Embedding"" IS NOT NULL
            ORDER BY ""Embedding"" <=> @vec::vector
            LIMIT @topK";

        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@vec",  vectorLiteral);
        cmd.Parameters.AddWithValue("@topK", topK * 3);

        var results = new List<(CrewTemplateEmbedding, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entry = ReadEntry(reader);
            var rawSimilarity = reader.GetDouble(reader.GetOrdinal("Similarity"));
            var boost = domainHint is not null && entry.Domain == domainHint
                ? sameDomainBoost
                : 1.0;
            results.Add((entry, rawSimilarity * boost));
        }

        return results
            .OrderByDescending(r => r.Item2)
            .Take(topK)
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ToVectorLiteral(float[] v) =>
        "[" + string.Join(",", v.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

    private static CrewTemplateEmbedding ReadEntry(System.Data.Common.DbDataReader r) => new()
    {
        Id           = r.GetGuid(r.GetOrdinal("Id")),
        TemplateName = r.GetString(r.GetOrdinal("TemplateName")),
        Domain       = r.GetString(r.GetOrdinal("Domain")),
        Summary      = r.GetString(r.GetOrdinal("Summary")),
        Embedding    = [],   // not fetched — not needed for grounding context
        CreatedAt    = r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("CreatedAt")),
    };

    private Npgsql.NpgsqlConnection NewConnection()
    {
        var cs = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        return new Npgsql.NpgsqlConnection(cs);
    }
}
