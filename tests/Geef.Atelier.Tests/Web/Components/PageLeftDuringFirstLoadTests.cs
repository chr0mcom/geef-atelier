using System.Runtime.CompilerServices;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application_IKnowledgeService = Geef.Atelier.Application.Crew.Knowledge.IKnowledgeService;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// Funktional 3 and 4 (Etappe 0, review round 2): a user who leaves the run detail page, the run list or the
/// workshop page while the first query is still running must not cause an error, and no live-update subscription may
/// be left behind for the closed page.
/// </summary>
public sealed class PageLeftDuringFirstLoadTests : TestContext
{
    private readonly CountingFeed _feed = new();

    public PageLeftDuringFirstLoadTests()
    {
        Services.AddSingleton<IRunUpdateFeed>(_feed);
        Services.AddScoped<CircuitDataGate>();
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task LeavingTheDetailPageDuringTheFirstLoadThrowsNothingAndSubscribesNothing()
    {
        var runService = new HeldDetailRunService(DetailsFor(Guid.NewGuid()));
        RegisterDetailServices(runService);
        RenderComponent<RunDetail>(p => p.Add(c => c.RunId, runService.RunId));
        await WaitUntilAsync(() => runService.Entered);

        DisposeComponents();
        runService.Hold.SetResult();
        await Task.Delay(300);

        if (Renderer.UnhandledException.IsCompleted)
            Assert.Fail((await Renderer.UnhandledException).ToString());
        Assert.Equal(0, _feed.RunWatchCalls);
        Assert.Equal(0, _feed.ActiveSubscriptions);
    }

    [Fact]
    public async Task LeavingTheRunListDuringTheFirstLoadLeavesNoSubscription()
    {
        var runService = new ConcurrencyProbeRunService { ImmediateCalls = 0 };
        Services.AddSingleton<IRunService>(runService);
        this.AddTestAuthorization().SetAuthorized("alice");
        RenderComponent<Runs>();
        await WaitUntilAsync(() => runService.Calls == 1);

        DisposeComponents();
        runService.Hold.SetResult();
        await Task.Delay(300);

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.Equal(0, _feed.ListWatchCalls);
        Assert.Equal(0, _feed.ActiveSubscriptions);
    }

    [Fact]
    public async Task AnOpenRunListStillSubscribesAndReleasesTheSubscriptionWhenClosed()
    {
        Services.AddSingleton<IRunService>(new ConcurrencyProbeRunService());
        this.AddTestAuthorization().SetAuthorized("alice");

        RenderComponent<Runs>();
        await WaitUntilAsync(() => _feed.ListWatchCalls == 1);

        Assert.Equal(1, _feed.ActiveSubscriptions);
        DisposeComponents();
        await WaitUntilAsync(() => _feed.ActiveSubscriptions == 0);
    }

    private void RegisterDetailServices(IRunService runService)
    {
        Services.AddSingleton(runService);
        Services.AddSingleton<IRunArtifactRepository>(new StubArtifactRepository([]));
        Services.AddSingleton<Application_IKnowledgeService>(new NoOpKnowledgeService());
        this.AddTestAuthorization().SetAuthorized("user");
    }

    private static RunDetails DetailsFor(Guid runId) => new(
        new RunEntity
        {
            Id = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running,
            BriefingText = "Test briefing",
            ConfigJson = "{}",
            CrewSnapshot = TestCrewSnapshots.RunnableJson,
        },
        []);

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

    private sealed class CountingFeed : IRunUpdateFeed
    {
        private int _active;
        private int _runWatchCalls;
        private int _listWatchCalls;

        public int ActiveSubscriptions => Volatile.Read(ref _active);
        public int RunWatchCalls => Volatile.Read(ref _runWatchCalls);
        public int ListWatchCalls => Volatile.Read(ref _listWatchCalls);

        public Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default)
        {
            var subscription = NewSubscription();
            Interlocked.Increment(ref _runWatchCalls);
            return Task.FromResult<IDisposable?>(subscription);
        }

        public IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated)
        {
            var subscription = NewSubscription();
            Interlocked.Increment(ref _listWatchCalls);
            return subscription;
        }

        public Task PublishRunUpdatedAsync(Guid runId, string? owner) => Task.CompletedTask;

        public Task<IDisposable?> WatchOrderAsync(ClaimsPrincipal user, Guid orderId, Func<Guid, Task> onUpdated, CancellationToken ct = default) =>
            Task.FromResult<IDisposable?>(null);

        public IDisposable WatchOrderList(ClaimsPrincipal user, Func<Guid, Task> onUpdated) => NewSubscription();

        public Task PublishOrderUpdatedAsync(Guid orderId, string? owner) => Task.CompletedTask;

        private Subscription NewSubscription()
        {
            Interlocked.Increment(ref _active);
            return new Subscription(this);
        }

        private sealed class Subscription(CountingFeed owner) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    Interlocked.Decrement(ref owner._active);
            }
        }
    }

    private sealed class HeldDetailRunService(RunDetails details) : IRunService
    {
        private readonly StubRunService _inner = new(details);

        public Guid RunId => details.Run.Id;

        public TaskCompletionSource Hold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Entered { get; private set; }

        public async Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default)
        {
            Entered = true;
            await Hold.Task;
            return await _inner.GetRunWithGroundingAsync(id, requestingUsername, cancellationToken);
        }

        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.ListRunWarningsAsync(id, requestingUsername, cancellationToken);
        public Task<RunEntity?> GetRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.GetRunAsync(id, requestingUsername, cancellationToken);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) => _inner.ListRunsAsync(limit, statusFilter, requestingUsername, cancellationToken);
        public Task<bool> CancelRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.CancelRunAsync(id, requestingUsername, cancellationToken);
        public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.GetRunDetailsAsync(id, requestingUsername, cancellationToken);
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.ResumeRunAsync(options, requestingUsername, cancellationToken);
        public Task<bool> DeleteRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => _inner.DeleteRunAsync(id, requestingUsername, cancellationToken);
    }
}
