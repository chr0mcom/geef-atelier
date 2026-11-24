using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores tool invocations with a per-run sequence assigned by the database. Inserts of one run are serialized by a
/// transaction-scoped advisory lock on the run id, so concurrent tool calls of a run never compute the same next
/// sequence; the retry on a unique violation remains as a safety net (for example rows written by an older version).
/// </summary>
internal sealed class ToolInvocationRepository(AtelierDbContext db) : IToolInvocationRepository
{
    internal const int MaxSequenceAttempts = 5;
    internal const string SequenceIndexName = "IX_tool_invocations_RunId_Sequence";

    /// <inheritdoc/>
    public async Task AddAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await InsertWithNextSequenceAsync(invocation, ct);
                return;
            }
            catch (Exception ex) when (attempt < MaxSequenceAttempts && ex.IsSequenceConflict())
            {
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolInvocation>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
        => await db.ToolInvocations
            .AsNoTracking()
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.Sequence)
            .ToListAsync(ct);

    private async Task InsertWithNextSequenceAsync(ToolInvocation invocation, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({invocation.RunId.ToString()}, 0))", ct);
        await InsertAsync(invocation, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    private Task<int> InsertAsync(ToolInvocation invocation, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO tool_invocations
                ("Id", "RunId", "IterationNumber", "ActorType", "ActorName", "ToolName", "ToolType",
                 "InputJson", "OutputExcerpt", "CostEur", "DurationMs", "Sequence", "Outcome", "CreatedAt")
            SELECT {invocation.Id}, {invocation.RunId}, {invocation.IterationNumber}, {invocation.ActorType}::text,
                   {invocation.ActorName}::text, {invocation.ToolName}::text, {invocation.ToolType}::text,
                   {invocation.InputJson}::text, {invocation.OutputExcerpt}::text, {invocation.CostEur}::numeric,
                   {invocation.DurationMs}, COALESCE(MAX("Sequence"), -1) + 1, {(int)invocation.Outcome},
                   {invocation.CreatedAt.ToUniversalTime()}
            FROM tool_invocations
            WHERE "RunId" = {invocation.RunId}
            """, ct);
}

internal static class ToolInvocationSequenceConflictExtensions
{
    /// <summary>True when <paramref name="exception"/> is a unique violation of the per-run sequence index.</summary>
    public static bool IsSequenceConflict(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                && pg.ConstraintName == ToolInvocationRepository.SequenceIndexName)
                return true;
        }
        return false;
    }
}
