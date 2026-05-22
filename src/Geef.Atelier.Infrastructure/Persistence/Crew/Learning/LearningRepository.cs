using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew.Learning;

/// <summary>
/// EF-based CRUD + raw-SQL cosine search for <see cref="LearningEntry"/> records.
/// Raw SQL is required for the vector(1536) embedding column because
/// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql 10.x.
/// </summary>
internal sealed class LearningRepository(AtelierDbContext context) : ILearningRepository
{
    public async Task<LearningEntry> CreateAsync(LearningEntry entry, float[] embedding, CancellationToken ct = default)
    {
        var entity = ToEntity(entry);
        context.LearningEntries.Add(entity);
        await context.SaveChangesAsync(ct);

        if (embedding.Length > 0)
            await SetEmbeddingAsync(entry.Id, embedding, ct);

        return entry;
    }

    public async Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await context.LearningEntries.FindAsync([id], ct);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default)
    {
        var entity = await context.LearningEntries
            .Where(e => e.SourceRunId == sourceRunId && e.Status == (int)LearningStatus.Proposed)
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default)
    {
        await context.LearningEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, (int)status)
                .SetProperty(e => e.ApprovedAt, approvedAt),
                ct);
    }

    public async Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default)
    {
        await context.LearningEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LearningRunId, learningRunId), ct);
    }

    public async Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default)
    {
        var vectorLiteral = "[" + string.Join(",",
            embedding.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

        const string sql = @"UPDATE ""LearningEntries"" SET ""Embedding"" = @vec::vector WHERE ""Id"" = @id";
        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@vec", vectorLiteral);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<LearningEntry>> ListAsync(
        LearningStatus? status = null,
        string? domain = null,
        string? owner = null,
        CancellationToken ct = default)
    {
        var query = context.LearningEntries.AsQueryable();
        if (status.HasValue) query = query.Where(e => e.Status == (int)status.Value);
        if (domain is not null) query = query.Where(e => e.Domain == domain);
        if (owner is not null) query = query.Where(e => e.OwnerUsername == owner);
        var entities = await query.OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
        float[] queryEmbedding,
        string? currentDomain,
        double sameDomainBoost,
        double crossDomainPenalty,
        int topK,
        CancellationToken ct = default)
    {
        var vectorLiteral = "[" + string.Join(",",
            queryEmbedding.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

        const string sql = @"
            SELECT ""Id"", ""Text"", ""SourceRunId"", ""LearningRunId"", ""Domain"",
                   ""Status"", ""StructuredFactsJson"", ""OwnerUsername"", ""CreatedAt"", ""ApprovedAt"",
                   1.0 - (""Embedding"" <=> @vec::vector) AS ""Similarity""
            FROM ""LearningEntries""
            WHERE ""Status"" = 1 AND ""Embedding"" IS NOT NULL
            ORDER BY ""Embedding"" <=> @vec::vector
            LIMIT @topK";

        await using var connection = NewConnection();
        await connection.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@vec", vectorLiteral);
        cmd.Parameters.AddWithValue("@topK", topK * 4); // fetch extra, re-rank after domain boost

        var results = new List<(LearningEntry, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entry = ReadEntry(reader);
            var rawSimilarity = reader.GetDouble(reader.GetOrdinal("Similarity"));
            var boost = currentDomain is not null && entry.Domain == currentDomain
                ? sameDomainBoost
                : crossDomainPenalty;
            results.Add((entry, rawSimilarity * boost));
        }

        return results
            .OrderByDescending(r => r.Item2)
            .Take(topK)
            .ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await context.LearningEntries.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LearningEntryEntity ToEntity(LearningEntry e) => new()
    {
        Id                  = e.Id,
        Text                = e.Text,
        SourceRunId         = e.SourceRunId,
        LearningRunId       = e.LearningRunId,
        Domain              = e.Domain,
        Status              = (int)e.Status,
        StructuredFactsJson = e.StructuredFactsJson,
        OwnerUsername       = e.OwnerUsername,
        CreatedAt           = e.CreatedAt,
        ApprovedAt          = e.ApprovedAt,
    };

    private static LearningEntry ToDomain(LearningEntryEntity e) => new(
        Id:                  e.Id,
        Text:                e.Text,
        SourceRunId:         e.SourceRunId ?? Guid.Empty,
        LearningRunId:       e.LearningRunId,
        Domain:              e.Domain,
        Status:              (LearningStatus)e.Status,
        StructuredFactsJson: e.StructuredFactsJson,
        OwnerUsername:       e.OwnerUsername,
        CreatedAt:           e.CreatedAt,
        ApprovedAt:          e.ApprovedAt);

    private static LearningEntry ReadEntry(System.Data.Common.DbDataReader r) => new(
        Id:                  r.GetGuid(r.GetOrdinal("Id")),
        Text:                r.GetString(r.GetOrdinal("Text")),
        SourceRunId:         r.IsDBNull(r.GetOrdinal("SourceRunId")) ? Guid.Empty : r.GetGuid(r.GetOrdinal("SourceRunId")),
        LearningRunId:       r.IsDBNull(r.GetOrdinal("LearningRunId")) ? null : r.GetGuid(r.GetOrdinal("LearningRunId")),
        Domain:              r.GetString(r.GetOrdinal("Domain")),
        Status:              (LearningStatus)r.GetInt32(r.GetOrdinal("Status")),
        StructuredFactsJson: r.GetString(r.GetOrdinal("StructuredFactsJson")),
        OwnerUsername:       r.GetString(r.GetOrdinal("OwnerUsername")),
        CreatedAt:           r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("CreatedAt")),
        ApprovedAt:          r.IsDBNull(r.GetOrdinal("ApprovedAt")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("ApprovedAt")));

    private Npgsql.NpgsqlConnection NewConnection()
    {
        var cs = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        return new Npgsql.NpgsqlConnection(cs);
    }
}
