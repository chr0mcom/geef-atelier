using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Persistence.Intake;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="IntakeShadowRepository"/> against real Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class IntakeShadowRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Pending_rows_are_added_once_listed_oldest_first_and_claimed_once()
    {
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var first = await AddOrderAsync();
        var second = await AddOrderAsync();
        await using var db = fixture.NewContext();
        var repository = new IntakeShadowRepository(db);

        await repository.AddPendingAsync(second, t0.AddSeconds(1));
        await repository.AddPendingAsync(first, t0);
        await repository.AddPendingAsync(first, t0.AddSeconds(5));

        var pending = (await repository.ListPendingAsync(DateTimeOffset.UtcNow, 2, 10_000)).ToList();
        Assert.True(pending.IndexOf(first) < pending.IndexOf(second));
        Assert.Equal(t0, (await LoadAsync(first)).CreatedAt, TimeSpan.FromMilliseconds(1));

        var now = DateTimeOffset.UtcNow;
        await using var db1 = fixture.NewContext();
        await using var db2 = fixture.NewContext();
        var claims = await Task.WhenAll(
            new IntakeShadowRepository(db1).TryClaimAsync(first, now, now.AddSeconds(-90), 2),
            new IntakeShadowRepository(db2).TryClaimAsync(first, now, now.AddSeconds(-90), 2));
        Assert.Equal(1, claims.Count(c => c));
        Assert.Equal(1, (await LoadAsync(first)).Attempts);
        Assert.DoesNotContain(first, await repository.ListPendingAsync(now.AddSeconds(-90), 2, 10_000));

        var later = now.AddMinutes(3);
        Assert.Contains(first, await repository.ListPendingAsync(later.AddSeconds(-90), 2, 10_000));
        Assert.True(await repository.TryClaimAsync(first, later, later.AddSeconds(-90), 2));
        var muchLater = later.AddMinutes(3);
        Assert.False(await repository.TryClaimAsync(first, muchLater, muchLater.AddSeconds(-90), 2));
        Assert.DoesNotContain(first, await repository.ListPendingAsync(muchLater.AddSeconds(-90), 2, 10_000));
    }

    [Fact]
    public async Task Completing_stores_the_result_and_counts_toward_the_daily_cap_except_skipped_rows()
    {
        var since = DateTimeOffset.UtcNow.AddYears(5);
        var done = await AddOrderAsync();
        var failed = await AddOrderAsync();
        var skipped = await AddOrderAsync();
        var open = await AddOrderAsync();
        await using var db = fixture.NewContext();
        var repository = new IntakeShadowRepository(db);
        foreach (var id in new[] { done, failed, skipped, open })
            await repository.AddPendingAsync(id, since);
        var before = await repository.CountCompletedSinceAsync(since);

        await repository.CompleteAsync(done, IntakeShadowStatuses.Done, 8_200, 1, """{"schemaVersion":1,"status":"Ok"}""", since.AddMinutes(1));
        await repository.CompleteAsync(failed, IntakeShadowStatuses.TimedOut, 40_000, null, null, since.AddMinutes(2));
        await repository.CompleteAsync(skipped, IntakeShadowStatuses.SkippedCap, null, null, null, since.AddMinutes(3));

        Assert.Equal(before + 2, await repository.CountCompletedSinceAsync(since));
        Assert.Equal(before, await repository.CountCompletedSinceAsync(since.AddMinutes(5)));
        var stored = await LoadAsync(done);
        Assert.Equal((IntakeShadowStatuses.Done, 8_200, 1), (stored.Status, stored.DurationMs!.Value, stored.QuestionCount!.Value));
        Assert.Contains("\"schemaVersion\"", stored.RecordJson, StringComparison.Ordinal);
        Assert.NotNull(stored.CompletedAt);
        Assert.DoesNotContain(done, await repository.ListPendingAsync(DateTimeOffset.MaxValue, 2, 10_000));
        Assert.Contains(open, await repository.ListPendingAsync(DateTimeOffset.MaxValue, 2, 10_000));
    }

    private async Task<Guid> AddOrderAsync()
    {
        var order = Order(NewUser());
        await using var db = fixture.NewContext();
        await new OrderRepository(db).CreateAsync(order, []);
        return order.Id;
    }

    private async Task<OrderIntakeShadowEntity> LoadAsync(Guid orderId)
    {
        await using var db = fixture.NewContext();
        return await db.OrderIntakeShadows.AsNoTracking().SingleAsync(s => s.OrderId == orderId);
    }
}
