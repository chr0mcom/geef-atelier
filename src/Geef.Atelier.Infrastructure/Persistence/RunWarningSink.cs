using Geef.Atelier.Core.Domain.Warnings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>
/// Stores run warnings. Warning messages are fixed sentences written for users, so only unambiguous key forms are
/// removed (<see cref="SecretRedactionExtensions.RedactKnownSecrets"/>); the wide opaque-token rule would cut long
/// display names out of the sentence.
/// </summary>
internal sealed class RunWarningSink(IServiceScopeFactory scopeFactory, ILogger<RunWarningSink> logger) : IRunWarningSink
{
    public async Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default)
    {
        var entity = new RunWarningEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Iteration = iteration,
            Source = source.Clip(RunWarningLimits.SourceLength),
            Code = code.Clip(RunWarningLimits.CodeLength),
            Message = message.RedactKnownSecrets().Clip(RunWarningLimits.MessageLength),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        logger.LogWarning("Run {RunId} warning {Code} ({Source}): {Message}", runId, entity.Code, entity.Source, entity.Message);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            db.RunWarnings.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store warning {Code} for run {RunId}; the run continues.", entity.Code, runId);
        }
    }
}
