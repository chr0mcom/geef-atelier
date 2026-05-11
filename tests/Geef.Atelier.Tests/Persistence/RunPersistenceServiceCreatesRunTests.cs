using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class RunPersistenceServiceCreatesRunTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CreateRunAsync_ReturnsValidGuid_AndPersistsWithPendingStatus()
    {
        await using var db     = fixture.NewContext();
        var             svc    = new RunPersistenceService(db);
        const string    brief  = "Test briefing.";
        const string    config = "{}";

        var runId = await svc.CreateRunAsync(brief, config, cancellationToken: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, runId);

        await using var verifyDb = fixture.NewContext();
        var run = await verifyDb.Runs.SingleOrDefaultAsync(r => r.Id == runId);

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.Equal(brief, run.BriefingText);
        Assert.Equal(config, run.ConfigJson);
        Assert.Equal(0, run.TokensTotal);
        Assert.Null(run.StartedAt);
        Assert.Null(run.CompletedAt);
        Assert.Null(run.FinalText);
        Assert.Null(run.ErrorMessage);
    }
}
