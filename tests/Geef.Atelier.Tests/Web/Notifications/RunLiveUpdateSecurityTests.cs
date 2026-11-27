using System.Collections.Concurrent;
using System.Security.Claims;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>
/// Live updates of runs are only for signed-in users and only for what they may see: the in-process feed used by the Blazor
/// components with the real notifier, the real registration and the real access check against Postgres.
/// </summary>
[Collection("Postgres")]
public sealed class RunLiveUpdateSecurityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private Guid _aliceRun;
    private Guid _bobRun;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddAtelierPersistence();
        services.AddAtelierApplication();
        services.AddSingleton<Geef.Atelier.Application.Crew.IModelCatalog, PassThroughModelCatalog>();
        services.AddSingleton<Geef.Atelier.Application.Sources.ISourceIntakeService, Geef.Atelier.Tests.Web.Components.Orders.FakeSourceIntakeService>();
        services.AddSingleton(TimeProvider.System);
        services.AddRunLiveUpdates();
        _provider = services.BuildServiceProvider();

        _aliceRun = await StoreRunAsync("alice");
        _bobRun = await StoreRunAsync("bob");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private async Task<Guid> StoreRunAsync(string owner)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
        var run = new RunEntity
        {
            Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Completed, BriefingText = $"Request of {owner}",
            ConfigJson = "{}", CreatedByUser = owner, CompletedAt = DateTimeOffset.UtcNow,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private IRunNotifier Notifier => _provider.GetRequiredService<IRunNotifier>();

    private IRunUpdateFeed Feed => _provider.GetRequiredService<IRunUpdateFeed>();

    private static ClaimsPrincipal Principal(string name, bool admin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within 15 s.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Feed_RunListAndDashboard_AreScopedToTheUser()
    {
        var aliceIds = new ConcurrentQueue<Guid>();
        var adminIds = new ConcurrentQueue<Guid>();
        var anonymousIds = new ConcurrentQueue<Guid>();
        using var a = Feed.WatchRunList(Principal("alice"), id => { aliceIds.Enqueue(id); return Task.CompletedTask; });
        using var r = Feed.WatchRunList(Principal("root", admin: true), id => { adminIds.Enqueue(id); return Task.CompletedTask; });
        using var n = Feed.WatchRunList(new ClaimsPrincipal(new ClaimsIdentity()), id => { anonymousIds.Enqueue(id); return Task.CompletedTask; });

        await Notifier.NotifyRunUpdatedAsync(_bobRun);
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await EventuallyAsync(() => aliceIds.Contains(_aliceRun) && adminIds.Count == 2);
        await Task.Delay(300);

        Assert.Equal([_aliceRun], aliceIds);
        Assert.Equal([_bobRun, _aliceRun], adminIds);
        Assert.Empty(anonymousIds);
    }

    [Fact]
    public async Task Feed_SingleRunWatch_RequiresAccess_AndStopsAfterDispose()
    {
        var hits = 0;

        Assert.Null(await Feed.WatchRunAsync(Principal("alice"), _bobRun, _ => Task.CompletedTask));
        Assert.Null(await Feed.WatchRunAsync(new ClaimsPrincipal(new ClaimsIdentity()), _aliceRun, _ => Task.CompletedTask));

        var subscription = await Feed.WatchRunAsync(Principal("alice"), _aliceRun, _ => { Interlocked.Increment(ref hits); return Task.CompletedTask; });
        Assert.NotNull(subscription);
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await EventuallyAsync(() => Volatile.Read(ref hits) == 1);
        subscription.Dispose();
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await Task.Delay(300);

        Assert.Equal(1, Volatile.Read(ref hits));
        Assert.NotNull(await Feed.WatchRunAsync(Principal("root", admin: true), _bobRun, _ => Task.CompletedTask));
    }
}
