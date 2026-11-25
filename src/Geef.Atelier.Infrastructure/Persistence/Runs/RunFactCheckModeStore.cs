using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Persistence.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence.Runs;

/// <summary>
/// Stores the fact-check mode in <c>Runs.FactCheckMode</c> (migration Step50). Writing the same mode again changes nothing; a
/// failure is logged and never stops the run, because the mode also travels in the run context and the report.
/// </summary>
internal sealed class RunFactCheckModeStore(IServiceScopeFactory scopeFactory, ILogger<RunFactCheckModeStore> logger) : IRunFactCheckModeStore
{
    /// <inheritdoc/>
    public async Task SetAsync(Guid runId, FactCheckMode mode, CancellationToken ct = default)
    {
        var value = mode.ToString();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            var affected = await db.Runs.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.FactCheckMode, value), ct);
            if (affected == 0)
                logger.LogWarning("Fact-check mode of run {RunId} not stored: the run does not exist.", runId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store the fact-check mode of run {RunId}; the run continues.", runId);
        }
    }
}
