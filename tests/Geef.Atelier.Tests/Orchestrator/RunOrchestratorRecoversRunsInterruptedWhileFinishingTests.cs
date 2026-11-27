using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Funktional 1 (Etappe 0, review round 2): a run whose process ended while its finishing steps ran has a terminal
/// status but no outcome. On the next start the orchestrator records the outcome (Converged for Completed with text,
/// DeliveredWithOpenPoints for Failed or Aborted with text, Failed without text) and a plain warning. Runs that
/// completed before the first run with an outcome (finished before Step47) stay untouched. The repair runs after the
/// reset of stale Running runs and on its own, so a failure in it never keeps a Running run from being reset. Real
/// Postgres.
/// </summary>
[Collection("Postgres")]
public sealed class RunOrchestratorRecoversRunsInterruptedWhileFinishingTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset LegacyCompletedAt = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstOutcomeCompletedAt = new(2021, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InterruptedCompletedAt = new(2021, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = 30_000)]
    public async Task Service_OnStart_RecordsAnOutcomeForRunsStoppedWhileFinishingButLeavesLegacyRunsAlone()
    {
        var legacy = await SeedAsync(RunStatus.Completed, "legacy text", null, LegacyCompletedAt);
        var legacyFailed = await SeedAsync(RunStatus.Failed, null, null, LegacyCompletedAt);
        await SeedAsync(RunStatus.Completed, "first", RunOutcome.Converged, FirstOutcomeCompletedAt);
        var completed = await SeedAsync(RunStatus.Completed, "finished text", null, InterruptedCompletedAt);
        var failedWithText = await SeedAsync(RunStatus.Failed, "best draft", null, InterruptedCompletedAt);
        var abortedWithText = await SeedAsync(RunStatus.Aborted, "draft", null, InterruptedCompletedAt);
        var failedWithoutText = await SeedAsync(RunStatus.Failed, null, null, InterruptedCompletedAt);
        var alreadyDone = await SeedAsync(RunStatus.Completed, "done", RunOutcome.Converged, InterruptedCompletedAt);

        await using (var host = new OrchestratorTestHost(fixture, new FakeLlmClient(),
            new Geef.Atelier.Core.Configuration.OrchestratorOptions { PollingInterval = TimeSpan.FromSeconds(60), MaxConcurrentRuns = 1 }))
        {
            await host.StartAsync();
            Guid[] interrupted = [completed, failedWithText, abortedWithText, failedWithoutText];
            await WaitUntilAsync(() => AllRepairedAsync(interrupted));
            await host.StopAsync();
        }

        Assert.Equal(RunOutcome.Converged, await OutcomeAsync(completed));
        Assert.Equal(RunOutcome.DeliveredWithOpenPoints, await OutcomeAsync(failedWithText));
        Assert.Equal(RunOutcome.DeliveredWithOpenPoints, await OutcomeAsync(abortedWithText));
        Assert.Equal(RunOutcome.Failed, await OutcomeAsync(failedWithoutText));
        Assert.Null(await OutcomeAsync(legacy));
        Assert.Null(await OutcomeAsync(legacyFailed));
        Assert.Equal(RunOutcome.Converged, await OutcomeAsync(alreadyDone));

        await using var db = fixture.NewContext();
        foreach (var id in new[] { completed, failedWithText, abortedWithText, failedWithoutText })
        {
            var warning = Assert.Single(await db.RunWarnings.Where(w => w.RunId == id).ToListAsync());
            Assert.Equal(RunWarningCodes.RestartedWhileFinishing, warning.Code);
            Assert.Equal(RunWarningSources.Run, warning.Source);
            Assert.Equal("The result may be incomplete because the service was restarted while finishing.", warning.Message);
        }
        foreach (var id in new[] { legacy, legacyFailed, alreadyDone })
            Assert.Empty(await db.RunWarnings.Where(w => w.RunId == id).ToListAsync());

        var statuses = await db.Runs.Where(r => r.Id == completed || r.Id == failedWithText || r.Id == abortedWithText)
            .Select(r => new { r.Id, r.Status, r.CompletedAt }).ToListAsync();
        Assert.Contains(statuses, r => r.Id == completed && r.Status == RunStatus.Completed);
        Assert.Contains(statuses, r => r.Id == failedWithText && r.Status == RunStatus.Failed);
        Assert.Contains(statuses, r => r.Id == abortedWithText && r.Status == RunStatus.Aborted);
        Assert.All(statuses, r => Assert.Equal(InterruptedCompletedAt, r.CompletedAt));
    }

    [Fact(Timeout = 30_000)]
    public async Task Service_OnStart_ResetsStaleRunningRunsEvenWhenTheFinishingRepairFails()
    {
        await SeedAsync(RunStatus.Completed, "first", RunOutcome.Converged, FirstOutcomeCompletedAt);
        await SeedAsync(RunStatus.Completed, "finished text", null, InterruptedCompletedAt);
        var stale = await SeedAsync(RunStatus.Running, null, null, InterruptedCompletedAt);

        await using (var host = new OrchestratorTestHost(fixture, new FakeLlmClient(),
            new Geef.Atelier.Core.Configuration.OrchestratorOptions { PollingInterval = TimeSpan.FromSeconds(60), MaxConcurrentRuns = 1 },
            services => services.AddSingleton<IRunWarningSink>(new ThrowingRunWarningSink())))
        {
            await host.StartAsync();
            await WaitUntilAsync(async () => await OutcomeAsync(stale) is not null);
            await host.StopAsync();
        }

        await using var db = fixture.NewContext();
        var staleRun = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == stale);
        Assert.Equal(RunStatus.Failed, staleRun.Status);
        Assert.Equal(RunOutcome.Failed, staleRun.Outcome);
    }

    private sealed class ThrowingRunWarningSink : IRunWarningSink
    {
        public Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("warning store unavailable");
    }

    private async Task<Guid> SeedAsync(RunStatus status, string? finalText, RunOutcome? outcome, DateTimeOffset completedAt)
    {
        await using var db = fixture.NewContext();
        var id = Guid.NewGuid();
        db.Runs.Add(new RunEntity
        {
            Id = id,
            CreatedAt = completedAt.AddMinutes(-10),
            StartedAt = completedAt.AddMinutes(-9),
            CompletedAt = completedAt,
            Status = status,
            BriefingText = "Interrupted while finishing",
            ConfigJson = "{}",
            FinalText = finalText,
            Outcome = outcome,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<RunOutcome?> OutcomeAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Runs.Where(r => r.Id == runId).Select(r => r.Outcome).SingleAsync();
    }

    /// <summary>
    /// True when every run of <paramref name="runIds"/> has its outcome and its warning. The repair goes through the runs in no fixed
    /// order and writes the warning after the outcome, so the host may only be stopped once all of them are through.
    /// </summary>
    private async Task<bool> AllRepairedAsync(IReadOnlyCollection<Guid> runIds)
    {
        await using var db = fixture.NewContext();
        return await db.Runs.CountAsync(r => runIds.Contains(r.Id) && r.Outcome != null) == runIds.Count
               && await db.RunWarnings.CountAsync(w => runIds.Contains(w.RunId)) >= runIds.Count;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The orchestrator did not recover the run in time.");
            await Task.Delay(100);
        }
    }
}
