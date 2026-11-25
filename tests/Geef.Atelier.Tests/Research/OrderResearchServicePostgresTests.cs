using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Infrastructure.Research;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// <see cref="OrderResearchService"/> with the real <see cref="OrderRepository"/> on Postgres: two runs that research the same order at
/// once store one research and both use it, a later run reads it without searching, and the daily limit counts what is stored.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class OrderResearchServicePostgresTests(PostgresFixture fixture)
{
    private const string Topic = "Mindestlohn 2026 Höhe";
    private const string Page = "Ab dem 1. Januar 2026 beträgt der gesetzliche Mindestlohn 13,90 Euro brutto je Stunde.";

    private static FakeSearch Search() => new(_ => new([new TavilyHit("https://example.org/lohn", "Lohn", Page)], null, 3)) { Delay = TimeSpan.FromMilliseconds(50) };

    private static FakeCondenser Condenser(string model) => new((_, pages) => new([new(1, pages[0].Id, Page)], new Dictionary<ResearchDropReason, int>(), model, null, 3));

    private static OrderResearchService Service(AtelierDbContext db, FakeSearch search, FakeCondenser condenser, ResearchOptions? options = null) =>
        new(new OrderRepository(db), search, condenser,
            Options.Create(options ?? new ResearchOptions { Mode = ResearchMode.On }), NullLogger<OrderResearchService>.Instance, TimeProvider.System);

    [Fact]
    public async Task Two_runs_at_once_store_one_research_and_both_use_it()
    {
        var order = await AddAsync(Order(NewUser(), text: "Schreib einen Aushang zum Mindestlohn.") with { ClarifyJson = Clarify(Topic) });
        await using var db1 = fixture.NewContext();
        await using var db2 = fixture.NewContext();

        var results = await Task.WhenAll(
            Service(db1, Search(), Condenser("first")).EnsureAsync(order, 1, false, [], null, CancellationToken.None),
            Service(db2, Search(), Condenser("second")).EnsureAsync(order, 1, false, [], null, CancellationToken.None));

        var stored = await LoadAsync(order.Id);
        Assert.NotNull(stored.ResearchJson);
        Assert.NotNull(stored.ResearchAt);
        Assert.All(results, r => Assert.Equal(stored.ResearchJson, ResearchJson.Serialize(r!)));

        await using var db3 = fixture.NewContext();
        var later = Search();
        var again = await Service(db3, later, Condenser("third")).EnsureAsync(stored, 2, false, [], null, CancellationToken.None);
        Assert.Equal(stored.ResearchJson, ResearchJson.Serialize(again!));
        Assert.Empty(later.Queries);
    }

    [Fact]
    public async Task The_daily_limit_counts_the_research_stored_today()
    {
        await using var probe = fixture.NewContext();
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var already = await new OrderRepository(probe).CountResearchSinceAsync(today);
        var options = new ResearchOptions { Mode = ResearchMode.On, DailyOrderCap = already + 1 };

        var first = await AddAsync(Order(NewUser()) with { ClarifyJson = Clarify(Topic) });
        var second = await AddAsync(Order(NewUser()) with { ClarifyJson = Clarify(Topic) });
        await using var db = fixture.NewContext();
        var search = Search();

        var a = await Service(db, search, Condenser("m"), options).EnsureAsync(first, 1, false, [], null, CancellationToken.None);
        var b = await Service(db, search, Condenser("m"), options).EnsureAsync(second, 1, false, [], null, CancellationToken.None);

        Assert.Equal(ResearchStatus.Found, a!.Status);
        Assert.Equal(ResearchStatus.DailyLimit, b!.Status);
        Assert.Single(search.Queries);
        Assert.Equal(ResearchJson.Serialize(b), (await LoadAsync(second.Id)).ResearchJson);
    }

    private static string Clarify(string topic) =>
        IntakeRecordJson.Serialize(IntakeRecord.Fallback(IntakeRecordStatus.Ok, null, 1, "m", BriefMode.AppendAnswers) with { ResearchTopics = [topic] });

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
