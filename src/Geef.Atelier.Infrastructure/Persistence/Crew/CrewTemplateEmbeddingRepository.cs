using System.Globalization;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

/// <summary>
/// Persistence implementation for crew-template embedding vectors used for crew-level deduplication.
/// Stores and queries embedding vectors in the <c>crew_template_embeddings</c> pgvector table.
/// </summary>
/// <remarks>
/// The underlying table and its HNSW index are provisioned by the composition migration (Task 7+).
/// Until that migration runs, this repository is a functional no-op: searches return empty results
/// and upserts log a warning — the materializer handles both cases gracefully.
/// </remarks>
internal sealed class CrewTemplateEmbeddingRepository(
    AtelierDbContext context,
    ILogger<CrewTemplateEmbeddingRepository> logger) : ICrewTemplateEmbeddingRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string TemplateName, double Similarity)>> SearchAsync(
        float[] queryEmbedding,
        string? currentDomain,
        double sameDomainBoost,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorLiteral = ToVectorLiteral(queryEmbedding);
            var domainParam   = currentDomain ?? string.Empty;

            const string sql = """
                SELECT
                    template_name,
                    CASE WHEN domain = @domain
                         THEN (1 - (embedding <=> @vec::vector)) * @boost
                         ELSE (1 - (embedding <=> @vec::vector))
                    END AS boosted_similarity
                FROM crew_template_embeddings
                ORDER BY boosted_similarity DESC
                LIMIT @topK
                """;

            await using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@vec",    vectorLiteral);
            cmd.Parameters.AddWithValue("@domain", domainParam);
            cmd.Parameters.AddWithValue("@boost",  sameDomainBoost);
            cmd.Parameters.AddWithValue("@topK",   topK);

            var results = new List<(string TemplateName, double Similarity)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name       = reader.GetString(0);
                var similarity = reader.GetDouble(1);
                results.Add((name, similarity));
            }

            return results;
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "42P01" /* undefined_table */)
        {
            // Table not yet migrated — return empty (dedup is skipped, not blocked).
            logger.LogDebug(
                "CrewTemplateEmbeddingRepository.SearchAsync: crew_template_embeddings table not yet available; returning empty.");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        string templateName,
        string domain,
        string summaryText,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorLiteral = ToVectorLiteral(embedding);

            const string sql = """
                INSERT INTO crew_template_embeddings (template_name, domain, summary_text, embedding, created_at)
                VALUES (@name, @domain, @summary, @vec::vector, now())
                ON CONFLICT (template_name) DO UPDATE
                    SET domain       = EXCLUDED.domain,
                        summary_text = EXCLUDED.summary_text,
                        embedding    = EXCLUDED.embedding,
                        updated_at   = now()
                """;

            await using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@name",    templateName);
            cmd.Parameters.AddWithValue("@domain",  domain);
            cmd.Parameters.AddWithValue("@summary", summaryText);
            cmd.Parameters.AddWithValue("@vec",     vectorLiteral);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "42P01" /* undefined_table */)
        {
            // Table not yet migrated — log and swallow (the materializer handles this gracefully).
            logger.LogWarning(
                "CrewTemplateEmbeddingRepository.UpsertAsync: crew_template_embeddings table not yet available; " +
                "embedding not stored for template '{Template}'.", templateName);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ToVectorLiteral(float[] v) =>
        "[" + string.Join(",", v.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

    private Npgsql.NpgsqlConnection NewConnection()
    {
        var cs = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        return new Npgsql.NpgsqlConnection(cs);
    }
}
