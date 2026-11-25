using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
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

    private async Task RemoveProfileCopiesAsync(string username, CancellationToken ct)
    {
        var runs = await db.Runs
            .Where(r => r.CreatedByUser == username && r.BriefingText.Contains(BriefingSections.ProfileHeading))
            .Select(r => new { r.Id, r.BriefingText })
            .ToListAsync(ct);
        foreach (var run in runs)
        {
            var without = BriefingSections.WithoutProfile(run.BriefingText);
            if (!string.Equals(without, run.BriefingText, StringComparison.Ordinal))
                await db.Runs.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.BriefingText, without), ct);
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
