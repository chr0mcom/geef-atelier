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
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// Live updates and user actions on the run pages (review findings Q6, Q12/A7, Q19 of Etappe 0 and finding 3 of the
/// second test review): a live reload and a filter click on the list, and a live reload of the detail page and a
/// delete or resume, never use the circuit's data services at the same time; the detail page reloads once after it has
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
    public async Task LiveReloadOfTheDetailPageAndADeleteNeverQueryAtTheSameTime()
    {
        var (probe, feed, runId) = RegisterProbe();
        var cut = await RenderSettledDetailAsync(probe, feed, runId);
        cut.Find("[data-testid='delete-run-button']").Click();
        cut.Find("[data-testid='delete-confirm-input']").Input(runId.ToString("N")[..8].ToUpperInvariant());

        var liveReload = Task.Run(() => feed.RunHandler!(runId));
        await WaitUntilAsync(() => probe.Calls == 3);
        var delete = cut.InvokeAsync(() => cut.Find("[data-testid='delete-confirm-button']").ClickAsync(new MouseEventArgs()));
        await Task.Delay(300);

        Assert.Equal(3, probe.Calls);
        probe.Hold.SetResult();
        await liveReload.WaitAsync(TimeSpan.FromSeconds(10));
        await delete.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(4, probe.Calls);
        Assert.Equal(1, probe.MaxActive);
        Assert.EndsWith("/runs", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public async Task ResumeAndALiveReloadOfTheDetailPageNeverQueryAtTheSameTime()
    {
        var (probe, feed, runId) = RegisterProbe();
        var cut = await RenderSettledDetailAsync(probe, feed, runId);
        cut.Find("[data-testid='resume-run-button']").Click();

        var resume = cut.InvokeAsync(() => cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new MouseEventArgs()));
        await WaitUntilAsync(() => probe.Calls == 3);
        var liveReload = Task.Run(() => feed.RunHandler!(runId));
        await Task.Delay(300);

        Assert.Equal(3, probe.Calls);
        probe.Hold.SetResult();
        await resume.WaitAsync(TimeSpan.FromSeconds(10));
        await liveReload.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(4, probe.Calls);
        Assert.Equal(1, probe.MaxActive);
        Assert.Empty(cut.FindAll("[data-testid='resume-error']"));
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

    private (ConcurrencyProbeRunService Probe, ScriptedFeed Feed, Guid RunId) RegisterProbe()
    {
        var runId = Guid.NewGuid();
        var details = new RunDetails(
            new RunEntity
            {
                Id = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Failed,
                BriefingText = "Test briefing",
                FinalText = "Draft.",
                CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
                CrewSnapshot = TestCrewSnapshots.RunnableJson,
            },
            []);
        var probe = new ConcurrencyProbeRunService(details.Run, details) { ImmediateCalls = 2 };
        var feed = new ScriptedFeed { RunSubscription = Task.FromResult<IDisposable?>(new TrackedSubscription()) };
        Services.AddSingleton<IRunService>(probe);
        Services.AddSingleton<IRunUpdateFeed>(feed);
        Services.AddScoped<CircuitDataGate>();
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
        return (probe, feed, runId);
    }

    private async Task<IRenderedComponent<RunDetail>> RenderSettledDetailAsync(ConcurrencyProbeRunService probe, ScriptedFeed feed, Guid runId)
    {
        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, runId));
        await WaitUntilAsync(() => feed.RunHandler is not null && probe.Calls == 2 && probe.Active == 0);
        cut.WaitForState(() => cut.FindAll("[data-testid='resume-run-button']").Count == 1, TimeSpan.FromSeconds(10));
        return cut;
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
                CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
                CrewSnapshot = TestCrewSnapshots.RunnableJson,
            },
            []);
        var runService = new CountingDetailRunService(new StubRunService(details), runId);
        Services.AddSingleton<IRunService>(runService);
        Services.AddSingleton<IRunUpdateFeed>(feed);
        Services.AddScoped<CircuitDataGate>();
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

        public Func<Guid, Task>? RunHandler { get; private set; }

        public Task<IDisposable?> RunSubscription { get; init; } = Task.FromResult<IDisposable?>(null);

        public CancellationToken LastToken { get; private set; }

        public Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default)
        {
            LastToken = ct;
            RunHandler = onUpdated;
            return RunSubscription;
        }

        public IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated)
        {
            ListHandler = onUpdated;
            return new TrackedSubscription();
        }

        public Task PublishRunUpdatedAsync(Guid runId, string? owner) => Task.CompletedTask;

        public Task<IDisposable?> WatchOrderAsync(ClaimsPrincipal user, Guid orderId, Func<Guid, Task> onUpdated, CancellationToken ct = default) =>
            Task.FromResult<IDisposable?>(null);

        public IDisposable WatchOrderList(ClaimsPrincipal user, Func<Guid, Task> onUpdated) => new TrackedSubscription();

        public Task PublishOrderUpdatedAsync(Guid orderId, string? owner) => Task.CompletedTask;
    }

    private sealed class CountingDetailRunService(IRunService inner, Guid runId) : IRunService
    {
        private int _detailLoads;

        public Guid RunId => runId;

        public int DetailLoads => Volatile.Read(ref _detailLoads);

        public Task<RunViewModel?> GetRunViewAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _detailLoads);
            return inner.GetRunViewAsync(id, requestingUsername, cancellationToken);
        }

        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.ListRunWarningsAsync(id, requestingUsername, cancellationToken);
        public Task<RunEntity?> GetRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.GetRunAsync(id, requestingUsername, cancellationToken);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) => inner.ListRunsAsync(limit, statusFilter, requestingUsername, cancellationToken);
        public Task<bool> CancelRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.CancelRunAsync(id, requestingUsername, cancellationToken);
        public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.GetRunDetailsAsync(id, requestingUsername, cancellationToken);
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => inner.ResumeRunAsync(options, requestingUsername, cancellationToken);
        public Task<bool> DeleteRunAsync(Guid id, string? requestingUsername, CancellationToken cancellationToken = default) => inner.DeleteRunAsync(id, requestingUsername, cancellationToken);
    }
}
