using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// The research methods of <see cref="OrderRepository"/> against real Postgres: the research of an order is stored once (the first
/// writer wins, a second call changes nothing) and the daily count covers exactly the orders stored at or after its start.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class OrderRepositoryResearchTests(PostgresFixture fixture)
{
    private const string First = """{"schemaVersion":1,"status":"Found"}""";
    private const string Second = """{"schemaVersion":1,"status":"Failed"}""";

    [Fact]
    public async Task Research_is_stored_once_and_a_second_call_changes_nothing()
    {
        var order = await AddAsync(Order(NewUser()));
        var at = DateTimeOffset.UtcNow;
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.True(await repository.TrySetResearchAsync(order.Id, First, at));
        Assert.False(await repository.TrySetResearchAsync(order.Id, Second, at.AddMinutes(1)));

        var stored = await LoadAsync(order.Id);
        Assert.Equal(First, stored.ResearchJson);
        Assert.Equal(at, stored.ResearchAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(order.UpdatedAt, stored.UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Two_simultaneous_writers_have_exactly_one_winner()
    {
        for (var round = 0; round < 5; round++)
        {
            var order = await AddAsync(Order(NewUser()));
            var at = DateTimeOffset.UtcNow;
            await using var db1 = fixture.NewContext();
            await using var db2 = fixture.NewContext();

            var results = await Task.WhenAll(
                new OrderRepository(db1).TrySetResearchAsync(order.Id, First, at),
                new OrderRepository(db2).TrySetResearchAsync(order.Id, Second, at));

            Assert.Equal(1, results.Count(r => r));
            var stored = await LoadAsync(order.Id);
            Assert.Equal(results[0] ? First : Second, stored.ResearchJson);
        }
    }

    [Fact]
    public async Task An_unknown_order_stores_nothing()
    {
        await using var db = fixture.NewContext();
        Assert.False(await new OrderRepository(db).TrySetResearchAsync(Guid.NewGuid(), First, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Blank_research_is_refused()
    {
        var order = await AddAsync(Order(NewUser()));
        await using var db = fixture.NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => new OrderRepository(db).TrySetResearchAsync(order.Id, " ", DateTimeOffset.UtcNow));
        Assert.Null((await LoadAsync(order.Id)).ResearchJson);
    }

    [Fact]
    public async Task The_count_covers_the_orders_stored_from_the_start_of_the_day_on()
    {
        var day = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Random.Shared.Next(0, 3_000));
        var yesterday = await AddAsync(Order(NewUser()));
        var atMidnight = await AddAsync(Order(NewUser()));
        var laterToday = await AddAsync(Order(NewUser()));
        var withoutResearch = await AddAsync(Order(NewUser()));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.True(await repository.TrySetResearchAsync(yesterday.Id, First, day.AddTicks(-TimeSpan.TicksPerMillisecond)));
        Assert.True(await repository.TrySetResearchAsync(atMidnight.Id, First, day));
        Assert.True(await repository.TrySetResearchAsync(laterToday.Id, First, day.AddHours(23)));

        var after = await repository.CountResearchSinceAsync(day.AddDays(1));
        Assert.Equal(2, await repository.CountResearchSinceAsync(day) - after);
        Assert.Equal(1, await repository.CountResearchSinceAsync(day.AddHours(1)) - after);
        Assert.Equal(3, await repository.CountResearchSinceAsync(day.AddDays(-1)) - after);
        Assert.Null((await LoadAsync(withoutResearch.Id)).ResearchAt);
    }

    [Fact]
    public async Task The_count_reads_an_offset_as_the_same_instant()
    {
        var day = new DateTimeOffset(2041, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Random.Shared.Next(0, 3_000));
        var order = await AddAsync(Order(NewUser()));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.True(await repository.TrySetResearchAsync(order.Id, First, day.ToOffset(TimeSpan.FromHours(2))));

        Assert.Equal(1, await repository.CountResearchSinceAsync(day) - await repository.CountResearchSinceAsync(day.AddTicks(TimeSpan.TicksPerMillisecond)));
    }

    private async Task<OrderEntity> AddAsync(OrderEntity order)
    {
        await using var db = fixture.NewContext();
        await new OrderRepository(db).CreateAsync(order, []);
        return order;
    }

    private async Task<OrderEntity> LoadAsync(Guid orderId)
    {
        await using var db = fixture.NewContext();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }
}
