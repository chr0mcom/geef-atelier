using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

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

    private Task<int> InsertWithNextSequenceAsync(ToolInvocation invocation, CancellationToken ct) =>
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
