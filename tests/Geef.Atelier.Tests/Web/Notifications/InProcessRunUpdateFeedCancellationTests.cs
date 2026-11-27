using System.Security.Claims;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>
/// Q19 (Etappe 0 review): a page that is closed while the feed still checks access must not leave a registration
/// behind in the singleton feed.
/// </summary>
public sealed class InProcessRunUpdateFeedCancellationTests
{
    private static readonly ClaimsPrincipal Alice = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test"));

    [Fact]
    public async Task CancelledWhileCheckingAccessReturnsNoSubscriptionAndNeverCallsTheHandler()
    {
        var run = Run();
        var runService = new ConcurrencyProbeRunService(run) { GetRunHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var feed = Feed(runService);
        var calls = 0;
        using var cts = new CancellationTokenSource();

        var watch = feed.WatchRunAsync(Alice, run.Id, _ => { Interlocked.Increment(ref calls); return Task.CompletedTask; }, cts.Token);
        cts.Cancel();
        var subscription = await watch.WaitAsync(TimeSpan.FromSeconds(10));
        await feed.PublishRunUpdatedAsync(run.Id, "alice");
        await Task.Delay(200);

        Assert.Null(subscription);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task AlreadyCancelledTokenSubscribesNothing()
    {
        var run = Run();
        var feed = Feed(new ConcurrencyProbeRunService(run));
        var calls = 0;

        var subscription = await feed.WatchRunAsync(Alice, run.Id, _ => { Interlocked.Increment(ref calls); return Task.CompletedTask; },
            new CancellationToken(canceled: true));
        await feed.PublishRunUpdatedAsync(run.Id, "alice");
        await Task.Delay(200);

        Assert.Null(subscription);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task ActiveSubscriberStillReceivesUpdates()
    {
        var run = Run();
        var feed = Feed(new ConcurrencyProbeRunService(run));
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = await feed.WatchRunAsync(Alice, run.Id, id => { received.TrySetResult(id); return Task.CompletedTask; });
        await feed.PublishRunUpdatedAsync(run.Id, "alice");

        Assert.NotNull(subscription);
        Assert.Equal(run.Id, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static RunEntity Run() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Running,
        BriefingText = "b",
        CreatedByUser = "alice",
    };

    private static InProcessRunUpdateFeed Feed(IRunService runService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runService);
        return new InProcessRunUpdateFeed(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InProcessRunUpdateFeed>.Instance);
    }
}
