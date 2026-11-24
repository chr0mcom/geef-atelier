using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application_IKnowledgeService = Geef.Atelier.Application.Crew.Knowledge.IKnowledgeService;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// Live updates and user actions on the run pages (review findings Q6, Q12/A7, Q19 of Etappe 0): a live reload and a
/// filter click never use the circuit's data services at the same time, the detail page reloads once after it has
/// subscribed (a final update between first load and subscription is not lost), and a subscription granted after the
/// page was closed is released at once.
/// </summary>
public sealed class LiveReloadDataAccessTests : TestContext
{
    [Fact]
    public async Task LiveReloadAndFilterClickOnTheRunListNeverQueryAtTheSameTime()
    {
        var probe = new ConcurrencyProbeRunService { ImmediateCalls = 1 };
        var feed = new ScriptedFeed();
        Services.AddSingleton<IRunService>(probe);
        Services.AddSingleton<IRunUpdateFeed>(feed);
        Services.AddScoped<CircuitDataGate>();
        this.AddTestAuthorization().SetAuthorized("alice");

        var cut = RenderComponent<Runs>();
        await WaitUntilAsync(() => feed.ListHandler is not null);
        Assert.Equal(1, probe.Calls);

        var liveReload = Task.Run(() => feed.ListHandler!(Guid.NewGuid()));
        await WaitUntilAsync(() => probe.Calls == 2);
        var filterClick = cut.InvokeAsync(() => cut.FindAll("button.filter-pill")[2].ClickAsync(new MouseEventArgs()));
        await Task.Delay(300);

        Assert.Equal(2, probe.Calls);
        probe.Hold.SetResult();
        await liveReload.WaitAsync(TimeSpan.FromSeconds(10));
        await filterClick.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, probe.Calls);
        Assert.Equal(1, probe.MaxActive);
    }

    [Fact]
    public void DetailPageReloadsOnceAfterItHasSubscribed()
    {
        var runService = Register(new ScriptedFeed { RunSubscription = Task.FromResult<IDisposable?>(new TrackedSubscription()) });

        RenderComponent<RunDetail>(p => p.Add(c => c.RunId, runService.RunId));

        Assert.Equal(2, runService.DetailLoads);
    }

    [Fact]
    public void DetailPageWithoutAccessToUpdatesDoesNotReloadAgain()
    {
        var runService = Register(new ScriptedFeed { RunSubscription = Task.FromResult<IDisposable?>(null) });

        RenderComponent<RunDetail>(p => p.Add(c => c.RunId, runService.RunId));

        Assert.Equal(1, runService.DetailLoads);
    }

    [Fact]
    public async Task SubscriptionGrantedAfterThePageWasClosedIsReleased()
    {
        var pending = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var feed = new ScriptedFeed { RunSubscription = pending.Task };
        var runService = Register(feed);
        RenderComponent<RunDetail>(p => p.Add(c => c.RunId, runService.RunId));

        DisposeComponents();
        var subscription = new TrackedSubscription();
        pending.SetResult(subscription);
        await WaitUntilAsync(() => subscription.Disposed);

        Assert.True(feed.LastToken.IsCancellationRequested);
        Assert.True(subscription.Disposed);
    }

    private CountingDetailRunService Register(ScriptedFeed feed)
    {
        var runId = Guid.NewGuid();
        var details = new RunDetails(
            new RunEntity
            {
                Id = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Running,
                BriefingText = "Test briefing",
                ConfigJson = "{}",
                CrewSnapshot = TestCrewSnapshots.KlassikJson,
            },
            []);
        var runService = new CountingDetailRunService(new StubRunService(details), runId);
        Services.AddSingleton<IRunService>(runService);
        Services.AddSingleton<IRunUpdateFeed>(feed);
        Services.AddScoped<CircuitDataGate>();
        Services.AddSingleton<IRunArtifactRepository>(new StubArtifactRepository([]));
        Services.AddSingleton<Application_IKnowledgeService>(new NoOpKnowledgeService());
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
        return runService;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within 10 s.");
            await Task.Delay(20);
        }
    }

    private sealed class TrackedSubscription : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class ScriptedFeed : IRunUpdateFeed
    {
        public Func<Guid, Task>? ListHandler { get; private set; }

        public Task<IDisposable?> RunSubscription { get; init; } = Task.FromResult<IDisposable?>(null);

        public CancellationToken LastToken { get; private set; }

        public Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default)
        {
            LastToken = ct;
            return RunSubscription;
        }

        public IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated)
        {
            ListHandler = onUpdated;
            return new TrackedSubscription();
        }

        public IDisposable WatchDashboard(ClaimsPrincipal user, bool allUsers, Func<DashboardEvent, Task> onEvent) => new TrackedSubscription();

        public Task PublishRunUpdatedAsync(Guid runId, string? owner) => Task.CompletedTask;

        public Task PublishDashboardEventAsync(DashboardEvent dashboardEvent) => Task.CompletedTask;
    }

    private sealed class CountingDetailRunService(IRunService inner, Guid runId) : IRunService
    {
        private int _detailLoads;

        public Guid RunId => runId;

        public int DetailLoads => Volatile.Read(ref _detailLoads);

        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _detailLoads);
            return inner.GetRunWithGroundingAsync(id, requestingUsername, cancellationToken);
        }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default) => inner.SubmitRunAsync(request, cancellationToken);
        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.ListRunWarningsAsync(id, requestingUsername, cancellationToken);
        public Task<RunEntity?> GetRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.GetRunAsync(id, requestingUsername, cancellationToken);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) => inner.ListRunsAsync(limit, statusFilter, requestingUsername, cancellationToken);
        public Task<bool> CancelRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.CancelRunAsync(id, requestingUsername, cancellationToken);
        public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.GetRunDetailsAsync(id, requestingUsername, cancellationToken);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default) => inner.GetWelcomeStatsAsync(requestingUsername, cancellationToken);
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => inner.ResumeRunAsync(options, requestingUsername, cancellationToken);
        public Task<bool> DeleteRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.DeleteRunAsync(id, requestingUsername, cancellationToken);
    }
}
