using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class AccountStore(AtelierDbContext db, IAtelierUserRepository users) : IAccountStore
{
    public Task AddAsync(AtelierUser user, CancellationToken ct) => InTransactionAsync(async () =>
    {
        await db.UserProfiles.Where(p => p.Username == user.Username).ExecuteDeleteAsync(ct);
        await users.AddAsync(user, ct);
    }, ct);

    public Task UpdateAsync(AtelierUser updated, string previousUsername, CancellationToken ct) => InTransactionAsync(async () =>
    {
        await users.UpdateAsync(updated, ct);
        if (string.Equals(previousUsername, updated.Username, StringComparison.Ordinal))
            return;
        await db.UserProfiles.Where(p => p.Username == updated.Username).ExecuteDeleteAsync(ct);
        await db.Database.ExecuteSqlAsync($"""UPDATE "UserProfiles" SET "Username" = {updated.Username} WHERE "Username" = {previousUsername}""", ct);
        await RemoveProfileCopiesAsync(previousUsername, ct);
    }, ct);

    public Task DeleteAsync(AtelierUser user, CancellationToken ct) => InTransactionAsync(async () =>
    {
        await users.DeleteAsync(user.UserId, ct);
        await db.UserProfiles.Where(p => p.Username == user.Username).ExecuteDeleteAsync(ct);
        await RemoveProfileCopiesAsync(user.Username, ct);
    }, ct);

    /// <summary>
    /// The runs of <paramref name="username"/> lose every copy of the profile: the block in the briefing, the briefing in the stored
    /// pipeline events (<c>PipelineStartedEvent.Input</c> and any other text that holds the block or the profile word for word), and
    /// what the fact check took from it into the report (<see cref="ProfileCopies.WithoutProfile"/>), into the stored findings
    /// (reasons and quotes, <see cref="ProfileCopies.Hidden"/>) and into the events (<see cref="ProfileCopies.WithoutProfileInEvent"/>).
    /// Runs whose briefing lost its block earlier are cleaned again with the profile still held in their events, so copies an earlier
    /// version left behind are removed as well.
    /// </summary>
    private async Task RemoveProfileCopiesAsync(string username, CancellationToken ct)
    {
        var runs = await db.Runs
            .Where(r => r.CreatedByUser == username
                && (r.BriefingText.Contains(BriefingSections.ProfileHeading) || r.BriefingText.Contains(BriefingSections.ProfileHiddenNote)))
            .Select(r => new { r.Id, r.BriefingText, r.ReportJson })
            .ToListAsync(ct);
        foreach (var run in runs)
        {
            var events = await db.Events
                .Where(e => e.RunId == run.Id)
                .Select(e => new { e.Id, e.PayloadJson })
                .ToListAsync(ct);
            var profile = BriefingSections.Parse(run.BriefingText).Profile
                ?? events.Select(e => ProfileCopies.ProfileInEvent(e.PayloadJson)).FirstOrDefault(p => p is not null);

            var without = BriefingSections.WithoutProfile(run.BriefingText);
            if (!string.Equals(without, run.BriefingText, StringComparison.Ordinal))
                await db.Runs.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.BriefingText, without), ct);

            if (RunReportJson.TryParse(run.ReportJson) is { FactCheck: not null } report)
            {
                var json = RunReportJson.Serialize(ProfileCopies.WithoutProfile(report, profile));
                await db.Runs.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReportJson, json), ct);
            }

            var findings = await db.Findings
                .Where(f => f.Category == FactFindingMetadata.FindingCategory && db.Iterations.Any(i => i.Id == f.IterationId && i.RunId == run.Id))
                .Select(f => new { f.Id, f.Quote })
                .ToListAsync(ct);
            foreach (var finding in findings)
            {
                var quote = ProfileCopies.HiddenQuote(finding.Quote, profile);
                await db.Findings.Where(f => f.Id == finding.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Message, ProfileCopies.HiddenReason)
                    .SetProperty(f => f.Quote, quote), ct);
            }

            foreach (var stored in events)
            {
                if (ProfileCopies.WithoutProfileInEvent(stored.PayloadJson, profile) is { } payload)
                    await db.Events.Where(e => e.Id == stored.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.PayloadJson, payload), ct);
            }
        }
    }

    private async Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
