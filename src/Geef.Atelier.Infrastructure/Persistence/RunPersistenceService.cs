using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunPersistenceService(AtelierDbContext db) : IRunPersistenceService
{
    public async Task<Guid> CreateRunAsync(string briefingText, string configJson, CancellationToken cancellationToken = default)
    {
        var run = new RunEntity
        {
            Id           = Guid.NewGuid(),
            CreatedAt    = DateTimeOffset.UtcNow,
            Status       = RunStatus.Pending,
            BriefingText = briefingText,
            ConfigJson   = configJson,
            TokensTotal  = 0,
            CostTotal    = 0m
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
}
