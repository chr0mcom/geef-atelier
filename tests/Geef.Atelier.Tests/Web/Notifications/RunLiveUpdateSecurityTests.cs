using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Hubs;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>
/// Live updates are only for signed-in users and only for what they may see: the SignalR hub (real hub, real
/// notifier, test server) and the in-process feed used by the Blazor components, both against real Postgres.
/// </summary>
[Collection("Postgres")]
public sealed class RunLiveUpdateSecurityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplication _app = null!;
    private TestServer _server = null!;
    private Guid _aliceRun;
    private Guid _bobRun;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "Geef.Atelier.Web",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        builder.Services.AddAtelierPersistence();
        builder.Services.AddAtelierApplication();
        builder.Services.AddSingleton<Geef.Atelier.Application.Crew.IModelCatalog, PassThroughModelCatalog>();
        builder.Services.AddScoped<IKnowledgeService, NoOpKnowledgeService>();
        builder.Services.AddAuthentication(HeaderAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRunLiveUpdates();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapHub<RunHub>("/hubs/runs").RequireAuthorization();
        await _app.StartAsync();
        _server = _app.GetTestServer();

        await using var scope = _app.Services.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunService>();
        _aliceRun = await runs.SubmitRunAsync(new SubmitRunRequest("Alice briefing", "{}", CreatedByUser: "alice"));
        _bobRun = await runs.SubmitRunAsync(new SubmitRunRequest("Bob briefing", "{}", CreatedByUser: "bob"));
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private HubConnection Connect(string? user, bool admin = false) =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/runs", o =>
            {
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                if (user is not null)
                    o.Headers[HeaderAuthHandler.UserHeader] = user;
                if (admin)
                    o.Headers[HeaderAuthHandler.AdminHeader] = "1";
            })
            .Build();

    private IRunNotifier Notifier => _app.Services.GetRequiredService<IRunNotifier>();

    private static ClaimsPrincipal Principal(string name, bool admin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<T> WithinAsync<T>(Task<T> task) =>
        await task.WaitAsync(TimeSpan.FromSeconds(15));

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
    public async Task Hub_RejectsAnonymousConnections()
    {
        await using var hub = Connect(user: null);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Hub_UserReceivesOwnRunUpdates_ButNoneOfAnotherUser()
    {
        await using var alice = Connect("alice");
        var received = new ConcurrentQueue<(string Method, Guid Id)>();
        var aliceListUpdate = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.On<Guid>("RunUpdated", id => received.Enqueue(("RunUpdated", id)));
        alice.On<Guid>("AnyRunUpdated", id =>
        {
            received.Enqueue(("AnyRunUpdated", id));
            if (id == _aliceRun)
                aliceListUpdate.TrySetResult(id);
        });
        await alice.StartAsync();
        await alice.InvokeAsync("JoinRunGroupAsync", _aliceRun);
        await alice.InvokeAsync("JoinRunsListGroupAsync");

        await Notifier.NotifyRunUpdatedAsync(_bobRun);
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await WithinAsync(aliceListUpdate.Task);

        Assert.DoesNotContain(received, r => r.Id == _bobRun);
        Assert.Contains(("RunUpdated", _aliceRun), received);
    }

    [Fact]
    public async Task Hub_UserCannotJoinAnotherUsersRunGroup()
    {
        await using var alice = Connect("alice");
        await alice.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("JoinRunGroupAsync", _bobRun));
        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task Hub_AdminViewIsOnlyForAdmins()
    {
        await using var alice = Connect("alice");
        await alice.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("JoinDashboardAllGroupAsync"));

        await using var admin = Connect("root", admin: true);
        var adminGot = new TaskCompletionSource<DashboardEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adminList = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        admin.On<DashboardEvent>("DashboardUpdated", e => adminGot.TrySetResult(e));
        admin.On<Guid>("AnyRunUpdated", id => adminList.TrySetResult(id));
        await admin.StartAsync();
        await admin.InvokeAsync("JoinDashboardAllGroupAsync");
        await admin.InvokeAsync("JoinRunGroupAsync", _bobRun);
        await admin.InvokeAsync("JoinRunsListGroupAsync");

        await Notifier.NotifyDashboardEventAsync(new DashboardEvent(DashboardEventKind.PressUpdated, "bob", null, null));
        await Notifier.NotifyRunUpdatedAsync(_bobRun);

        Assert.Equal("bob", (await WithinAsync(adminGot.Task)).Username);
        Assert.Equal(_bobRun, await WithinAsync(adminList.Task));
    }

    [Fact]
    public async Task Hub_PersonalDashboardGroupIsDerivedFromTheSignedInUser()
    {
        await using var alice = Connect("alice");
        var events = new ConcurrentQueue<DashboardEvent>();
        var own = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.On<DashboardEvent>("DashboardUpdated", e =>
        {
            events.Enqueue(e);
            if (e.Username == "alice")
                own.TrySetResult();
        });
        await alice.StartAsync();
        await alice.InvokeAsync("JoinDashboardGroupAsync");

        await Notifier.NotifyDashboardEventAsync(new DashboardEvent(DashboardEventKind.PressUpdated, "bob", null, null));
        await Notifier.NotifyDashboardEventAsync(new DashboardEvent(DashboardEventKind.PressUpdated, "alice", null, null));
        await own.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.All(events, e => Assert.Equal("alice", e.Username));
    }

    [Fact]
    public async Task Feed_RunListAndDashboard_AreScopedToTheUser()
    {
        var feed = _app.Services.GetRequiredService<IRunUpdateFeed>();
        var aliceIds = new ConcurrentQueue<Guid>();
        var adminIds = new ConcurrentQueue<Guid>();
        var aliceEvents = new ConcurrentQueue<DashboardEvent>();
        using var a = feed.WatchRunList(Principal("alice"), id => { aliceIds.Enqueue(id); return Task.CompletedTask; });
        using var r = feed.WatchRunList(Principal("root", admin: true), id => { adminIds.Enqueue(id); return Task.CompletedTask; });
        using var d = feed.WatchDashboard(Principal("alice"), allUsers: true, e => { aliceEvents.Enqueue(e); return Task.CompletedTask; });

        await Notifier.NotifyRunUpdatedAsync(_bobRun);
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await Notifier.NotifyDashboardEventAsync(new DashboardEvent(DashboardEventKind.PressUpdated, "bob", null, null));
        await Notifier.NotifyDashboardEventAsync(new DashboardEvent(DashboardEventKind.PressUpdated, "alice", null, null));
        await EventuallyAsync(() => aliceIds.Contains(_aliceRun) && adminIds.Count == 2 && aliceEvents.Any(e => e.Username == "alice"));

        Assert.Equal([_aliceRun], aliceIds);
        Assert.Equal([_bobRun, _aliceRun], adminIds);
        Assert.Equal(["alice"], aliceEvents.Select(e => e.Username));
    }

    [Fact]
    public async Task Feed_SingleRunWatch_RequiresAccess_AndStopsAfterDispose()
    {
        var feed = _app.Services.GetRequiredService<IRunUpdateFeed>();
        var hits = 0;

        Assert.Null(await feed.WatchRunAsync(Principal("alice"), _bobRun, _ => Task.CompletedTask));
        Assert.Null(await feed.WatchRunAsync(new ClaimsPrincipal(new ClaimsIdentity()), _aliceRun, _ => Task.CompletedTask));

        var subscription = await feed.WatchRunAsync(Principal("alice"), _aliceRun, _ => { Interlocked.Increment(ref hits); return Task.CompletedTask; });
        Assert.NotNull(subscription);
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await EventuallyAsync(() => Volatile.Read(ref hits) == 1);
        subscription.Dispose();
        await Notifier.NotifyRunUpdatedAsync(_aliceRun);
        await Task.Delay(300);

        Assert.Equal(1, Volatile.Read(ref hits));
        Assert.NotNull(await feed.WatchRunAsync(Principal("root", admin: true), _bobRun, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Feed_FailingSubscriberDoesNotBlockOthers()
    {
        var feed = _app.Services.GetRequiredService<IRunUpdateFeed>();
        var delivered = false;
        using var bad = feed.WatchRunList(Principal("alice"), _ => throw new InvalidOperationException("boom"));
        using var good = feed.WatchRunList(Principal("alice"), _ => { delivered = true; return Task.CompletedTask; });

        await Notifier.NotifyRunUpdatedAsync(_aliceRun);

        await EventuallyAsync(() => Volatile.Read(ref delivered));
    }

    [Fact]
    public async Task Feed_RunsOneHandlerAtATimePerSubscriber_AndDoesNotBlockThePublisher()
    {
        var feed = _app.Services.GetRequiredService<IRunUpdateFeed>();
        var running = 0;
        var maxParallel = 0;
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var slow = feed.WatchRunList(Principal("root", admin: true), async _ =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxParallel, now);
            await release.Task;
            Interlocked.Increment(ref calls);
            Interlocked.Decrement(ref running);
        });

        var publish = Task.WhenAll(
            Notifier.NotifyRunUpdatedAsync(_aliceRun),
            Notifier.NotifyRunUpdatedAsync(_bobRun),
            Notifier.NotifyRunUpdatedAsync(_aliceRun));
        await publish.WaitAsync(TimeSpan.FromSeconds(15));
        release.SetResult();
        await EventuallyAsync(() => Volatile.Read(ref calls) >= 2 && Volatile.Read(ref running) == 0);

        Assert.Equal(1, maxParallel);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";
        public const string UserHeader = "X-Test-User";
        public const string AdminHeader = "X-Test-Admin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            if (Request.Headers.ContainsKey(AdminHeader))
                claims.Add(new Claim(ClaimTypes.Role, "admin"));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
