using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunRepository(AtelierDbContext db) : IRunRepository
{
    /// <inheritdoc/>
    public async Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
        => await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default)
    {
        var q = db.Runs.AsNoTracking();
        if (statusFilter is { } s)
            q = q.Where(r => r.Status == s);
        if (username is not null)
            q = q.Where(r => r.CreatedByUser == username);
        return await q.OrderByDescending(r => r.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var affected = await db.Runs
            .Where(r => r.Id == runId
                     && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running)
                     && !r.CancellationRequested)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancellationRequested, true), cancellationToken);
        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null) return null;

        var iterations = await db.Iterations
            .AsNoTracking()
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.IterationNumber)
            .ToListAsync(cancellationToken);

        var iterationIds = iterations.Select(i => i.Id).ToList();
        var findings = await db.Findings
            .AsNoTracking()
            .Where(f => iterationIds.Contains(f.IterationId))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        var iterationsWithFindings = iterations
            .Select(i => new IterationWithFindings(
                i,
                findings.Where(f => f.IterationId == i.Id).ToList()))
            .ToList();

        return new RunDetails(run, iterationsWithFindings);
    }

    /// <inheritdoc/>
    public async Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default)
    {
        var startOfMonth = new DateTimeOffset(
            DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        IQueryable<RunEntity> baseQ = db.Runs.Where(r => r.CreatedAt >= startOfMonth);
        if (username is not null)
            baseQ = baseQ.Where(r => r.CreatedByUser == username);

        var runsThisMonth = await baseQ.CountAsync(cancellationToken);

        var completedThisMonth = await baseQ
            .Where(r => r.Status == RunStatus.Completed)
            .CountAsync(cancellationToken);

        var convergenceRate = runsThisMonth > 0 ? (double)completedThisMonth / runsThisMonth : 0.0;

        var runIds = baseQ.Select(r => r.Id);
        var totalIterationsThisMonth = await db.Iterations
            .Where(i => runIds.Contains(i.RunId))
            .CountAsync(cancellationToken);

        var avgIterations = runsThisMonth > 0 ? (double)totalIterationsThisMonth / runsThisMonth : 0.0;

        var totalCostThisMonth = await baseQ
            .Where(r => r.TotalCostEur != null)
            .SumAsync(r => r.TotalCostEur!.Value, cancellationToken);

        // Studio-analysis stats are shared and never scoped by user.
        var studioAnalysesThisMonth = await db.TemplateStudioAnalyses
            .Where(a => a.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken);

        var studioCostThisMonth = await db.TemplateStudioAnalyses
            .Where(a => a.CreatedAt >= startOfMonth)
            .SumAsync(a => a.CostEur ?? 0m, cancellationToken);

        return new WelcomeStats(runsThisMonth, convergenceRate, avgIterations, totalCostThisMonth,
            studioAnalysesThisMonth, studioCostThisMonth);
    }
}
