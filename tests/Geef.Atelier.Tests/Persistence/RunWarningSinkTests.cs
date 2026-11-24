using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// <see cref="RunWarningSink"/> against real Postgres: it stores warnings in their own scope, a failed write is
/// swallowed and leaves later writes working, and an unreachable database never throws into the run.
/// </summary>
[Collection("Postgres")]
public sealed class RunWarningSinkTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StoresWarningsAndListsThemInOrder()
    {
        var runId = await CreateRunAsync();
        var sink = new RunWarningSink(fixture.NewScopeFactory(), NullLogger<RunWarningSink>.Instance);

        await sink.ReportAsync(runId, RunWarningSources.Grounding, RunWarningCodes.GroundingProviderSkipped, "first", iteration: null);
        await sink.ReportAsync(runId, RunWarningSources.Review, RunWarningCodes.ReviewerFailed, "second", iteration: 2);

        await using var db = fixture.NewContext();
        var warnings = await new RunWarningRepository(db).ListByRunAsync(runId);
        Assert.Equal(["first", "second"], warnings.Select(w => w.Message).ToList());
        Assert.Equal(2, warnings[1].Iteration);
        Assert.Equal(RunWarningCodes.ReviewerFailed, warnings[1].Code);
    }

    [Fact]
    public async Task FailedWriteIsSwallowedAndTheNextWriteStillWorks()
    {
        var runId = await CreateRunAsync();
        var sink = new RunWarningSink(fixture.NewScopeFactory(), NullLogger<RunWarningSink>.Instance);

        await sink.ReportAsync(Guid.NewGuid(), RunWarningSources.Llm, RunWarningCodes.LlmFailover, "orphan run violates the foreign key");
        await sink.ReportAsync(runId, RunWarningSources.Llm, RunWarningCodes.LlmFailover, "kept");

        await using var db = fixture.NewContext();
        var warnings = await db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync();
        Assert.Equal("kept", Assert.Single(warnings).Message);
    }

    [Fact]
    public async Task LongMessagesAreCutToTheColumnLimit()
    {
        var runId = await CreateRunAsync();
        var sink = new RunWarningSink(fixture.NewScopeFactory(), NullLogger<RunWarningSink>.Instance);

        await sink.ReportAsync(runId, new string('s', 300), new string('c', 300), new string('m', 5000));

        await using var db = fixture.NewContext();
        var warning = await db.RunWarnings.AsNoTracking().SingleAsync(w => w.RunId == runId);
        Assert.Equal((100, 100, 1000), (warning.Source.Length, warning.Code.Length, warning.Message.Length));
    }

    [Fact]
    public async Task UnreachableDatabaseNeverThrowsIntoTheRun()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(o => o
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2")
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        var sink = new RunWarningSink(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunWarningSink>.Instance);

        var write = sink.ReportAsync(Guid.NewGuid(), RunWarningSources.Grounding, RunWarningCodes.GroundingProviderSkipped, "lost");

        await write.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(write.IsCompletedSuccessfully);
    }

    private async Task<Guid> CreateRunAsync()
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync("warning sink test", "{}");
    }
}
