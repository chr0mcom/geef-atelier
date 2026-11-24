using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary><see cref="SourceRepository"/> against real Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SourceRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StoresWarningsAsJsonAndLoadsTextOnlyWhenAsked()
    {
        var source = Source(NewUser(), text: "[[page 1]]\nHello");
        await using var db = fixture.NewContext();
        var repo = new SourceRepository(db);
        await repo.AddAsync(source);

        var withText = (await repo.GetAsync(source.Id, includeText: true))!;
        var withoutText = (await repo.GetAsync(source.Id, includeText: false))!;
        Assert.Equal("[[page 1]]\nHello", withText.ExtractedText);
        Assert.Null(withoutText.ExtractedText);
        var warning = Assert.Single(withoutText.Warnings);
        Assert.Equal(SourceWarningCodes.PagesWithoutText, warning.Code);
        Assert.Equal([2], warning.Pages);
        Assert.Equal(SourceKind.Markdown, withoutText.Kind);

        var json = await db.Database.SqlQuery<string>($"""SELECT "WarningsJson"::text AS "Value" FROM "Sources" WHERE "Id" = {source.Id}""").SingleAsync();
        Assert.Contains("\"code\"", json, StringComparison.Ordinal);
        var kind = await db.Database.SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "Sources" WHERE "Id" = {source.Id}""").SingleAsync();
        Assert.Equal("Markdown", kind);
    }

    [Fact]
    public async Task FindByHashIsScopedToTheOwner()
    {
        var sha = new string('c', 63) + "1";
        var mine = Source(NewUser(), sha);
        var theirs = Source(NewUser(), sha);
        await using var db = fixture.NewContext();
        var repo = new SourceRepository(db);
        await repo.AddAsync(mine);
        await repo.AddAsync(theirs);

        Assert.Equal(mine.Id, (await repo.FindByHashAsync(mine.OwnerUsername, sha))!.Id);
        Assert.Equal(theirs.Id, (await repo.FindByHashAsync(theirs.OwnerUsername, sha))!.Id);
        Assert.Null(await repo.FindByHashAsync(NewUser(), sha));
    }

    [Fact]
    public async Task LinkedSourcesAreNotDeletedOrListedAsExpired()
    {
        var user = NewUser();
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var linked = Source(user, expiresAt: past);
        var loose = Source(user, expiresAt: past);
        var future = Source(user, expiresAt: DateTimeOffset.UtcNow.AddHours(5));
        var order = Order(user);
        await using var db = fixture.NewContext();
        var repo = new SourceRepository(db);
        await repo.AddAsync(linked);
        await repo.AddAsync(loose);
        await repo.AddAsync(future);
        db.Orders.Add(order);
        db.OrderSources.Add(Link(order.Id, linked.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var expired = await repo.ListExpiredUnattachedAsync(DateTimeOffset.UtcNow, 1000);
        Assert.Contains(expired, s => s.Id == loose.Id);
        Assert.DoesNotContain(expired, s => s.Id == linked.Id || s.Id == future.Id);
        Assert.All(expired, s => Assert.Null(s.ExtractedText));

        Assert.True(await repo.IsAttachedAsync(linked.Id));
        Assert.False(await repo.DeleteIfUnattachedAsync(linked.Id));
        Assert.True(await repo.DeleteIfUnattachedAsync(loose.Id));
        Assert.False(await repo.DeleteIfUnattachedAsync(loose.Id));
    }

    [Fact]
    public async Task ListsOrderSourcesByPositionAndRunUsages()
    {
        var user = NewUser();
        var a = Source(user);
        var b = Source(user);
        var order = Order(user);
        var run = Run(user, order.Id, 1);
        await using var db = fixture.NewContext();
        var repo = new SourceRepository(db);
        await repo.AddAsync(a);
        await repo.AddAsync(b);
        await new OrderRepository(db).CreateWithFirstRunAsync(order, [Link(order.Id, a.Id, 1), Link(order.Id, b.Id, 0, "1-2")], run);
        await repo.AddRunUsagesAsync(
        [
            new RunSourceUsageEntity { Id = Guid.NewGuid(), RunId = run.Id, SourceId = a.Id, FileName = "a", Mode = SourceUsageMode.FullText, Target = "writer", CharsIncluded = 5, Position = 1, CreatedAt = DateTimeOffset.UtcNow },
            new RunSourceUsageEntity { Id = Guid.NewGuid(), RunId = run.Id, SourceId = b.Id, FileName = "b", Mode = SourceUsageMode.FullTextWithMap, Target = "writer", CharsIncluded = 7, PagesIncluded = "1-2", Position = 0, CreatedAt = DateTimeOffset.UtcNow },
        ]);

        var items = await repo.ListForOrderAsync(order.Id, includeText: true);
        Assert.Equal([b.Id, a.Id], items.Select(i => i.Source.Id));
        Assert.Equal("1-2", items[0].Link.PageSelection);
        Assert.NotNull(items[0].Source.ExtractedText);

        var usages = await repo.ListRunUsagesAsync(run.Id);
        Assert.Equal(["b", "a"], usages.Select(u => u.FileName));
        Assert.Equal(SourceUsageMode.FullTextWithMap, usages[0].Mode);
    }

    [Fact]
    public async Task ReleasesOriginalsOnlyWhenEveryOrderFinishedLongAgo()
    {
        var user = NewUser();
        var old = DateTimeOffset.UtcNow.AddDays(-40);
        var finished = Source(user, storagePath: "aa/finished.pdf");
        var active = Source(user, storagePath: "aa/active.pdf");
        var recent = Source(user, storagePath: "aa/recent.pdf");
        var finishedOrder = Order(user, at: old);
        var activeOrder = Order(user, at: old);
        var recentOrder = Order(user);
        await using var db = fixture.NewContext();
        var repo = new SourceRepository(db);
        await repo.AddAsync(finished);
        await repo.AddAsync(active);
        await repo.AddAsync(recent);
        var orders = new OrderRepository(db);
        await orders.CreateWithFirstRunAsync(finishedOrder, [Link(finishedOrder.Id, finished.Id)], Run(user, finishedOrder.Id, 1, status: RunStatus.Completed, at: old));
        await orders.CreateWithFirstRunAsync(activeOrder, [Link(activeOrder.Id, active.Id)], Run(user, activeOrder.Id, 1, status: RunStatus.Running, at: old));
        await orders.CreateWithFirstRunAsync(recentOrder, [Link(recentOrder.Id, recent.Id)], Run(user, recentOrder.Id, 1, status: RunStatus.Completed));

        var releasable = await repo.ListReleasableOriginalsAsync(DateTimeOffset.UtcNow.AddDays(-30), 1000);
        Assert.Contains(releasable, r => r.SourceId == finished.Id && r.StoragePath == "aa/finished.pdf");
        Assert.DoesNotContain(releasable, r => r.SourceId == active.Id || r.SourceId == recent.Id);

        await repo.ClearStoragePathAsync(finished.Id);
        Assert.Null((await repo.GetAsync(finished.Id, false))!.StoragePath);
    }
}
