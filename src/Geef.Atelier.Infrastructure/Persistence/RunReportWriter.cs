using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunReportWriter(IServiceScopeFactory scopeFactory, ILogger<RunReportWriter> logger) : IRunReportWriter
{
    public async Task WriteAsync(Guid runId, string reportJson, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            var affected = await db.Runs.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReportJson, reportJson), ct);
            if (affected == 0)
                logger.LogWarning("Report of run {RunId} not stored: the run does not exist.", runId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store the report of run {RunId}; the run continues.", runId);
        }
    }
}
