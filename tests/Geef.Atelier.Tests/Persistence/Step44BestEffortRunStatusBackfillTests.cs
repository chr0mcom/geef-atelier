using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class Step44BestEffortRunStatusBackfillTests(PostgresFixture fixture)
{
    private const string AbortMessage    = "Aborted due to critical reviewer finding";
    private const string MaxIterMessage  = "Pipeline stopped: maximum iterations reached";
    private const string RestartMessage  = "Service restarted";

    [Fact]
    public async Task BackfillSql_MapsAbortedBeforeFailed_AndLeavesHealthyRowsUntouched()
    {
        await using var db  = fixture.NewContext();
        var             svc = new RunPersistenceService(db);

        var abortedVictim  = await SeedRunAsync(db, svc, RunStatus.Completed, AbortMessage);
        var failedVictim   = await SeedRunAsync(db, svc, RunStatus.Completed, MaxIterMessage);
        var healthyRun     = await SeedRunAsync(db, svc, RunStatus.Completed, errorMessage: null);
        var genuineFailure = await SeedRunAsync(db, svc, RunStatus.Failed, RestartMessage);

        foreach (var sql in ExtractUpSqlStatements())
            await db.Database.ExecuteSqlRawAsync(sql);

        await using var verify = fixture.NewContext();
        Assert.Equal(RunStatus.Aborted,   (await verify.Runs.SingleAsync(r => r.Id == abortedVictim)).Status);
        Assert.Equal(RunStatus.Failed,    (await verify.Runs.SingleAsync(r => r.Id == failedVictim)).Status);
        Assert.Equal(RunStatus.Completed, (await verify.Runs.SingleAsync(r => r.Id == healthyRun)).Status);
        Assert.Equal(RunStatus.Failed,    (await verify.Runs.SingleAsync(r => r.Id == genuineFailure)).Status);
    }

    /// <summary>
    /// Extracts the raw SQL of the migration's <c>Up</c> operations in declaration order, so the
    /// test exercises the exact statements (including the Aborted-before-Failed ordering) instead
    /// of a duplicated copy.
    /// </summary>
    private static IReadOnlyList<string> ExtractUpSqlStatements()
    {
        var operations = new Step44BestEffortRunStatusBackfill().UpOperations;
        var statements = operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
        Assert.Equal(2, statements.Count);
        Assert.Contains("'Aborted'", statements[0]);
        Assert.Contains("'Failed'",  statements[1]);
        return statements;
    }

    private static async Task<Guid> SeedRunAsync(
        AtelierDbContext db, RunPersistenceService svc, RunStatus status, string? errorMessage)
    {
        var runId = await svc.CreateRunAsync("Backfill seed run.", "{}", cancellationToken: CancellationToken.None);
        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       status)
                .SetProperty(r => r.ErrorMessage, errorMessage));
        return runId;
    }
}
