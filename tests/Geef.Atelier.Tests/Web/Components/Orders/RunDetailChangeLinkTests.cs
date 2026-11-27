using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// A failed or stopped run of the direct path that belongs to an order is changed on the order page, not resumed: the run page links to
/// "Change something" there while changing is switched on for the owner; otherwise, and for other runs, "Resume" stays. A change that
/// already has its own text is never offered to resume (the same change would be made twice); a sentence says where to change it.
/// </summary>
public sealed class RunDetailChangeLinkTests : TestContext
{
    private readonly Guid _runId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    private IRenderedComponent<RunDetail> Render(string? template, Guid? orderId, ReviseMode revise, bool withOptions = true, string? finalText = "# Text", int version = 1, string viewer = "alice", string? revisionHint = null, params string[] roles)
    {
        var snapshot = new CrewSnapshot(CrewSnapshot.CurrentSchemaVersion, template, TestLoop.Writer, [TestLoop.Reviewers[0]],
            EvaluationStrategy.Parallel, null);
        var run = new RunEntity
        {
            Id = _runId, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Failed, BriefingText = "Test briefing",
            FinalText = finalText, CreatedByUser = "alice", OrderId = orderId, CrewTemplateName = template, OrderVersion = orderId is null ? null : version, RevisionHint = revisionHint,
            CrewSnapshot = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }),
        };
        Services.AddSingleton<IRunService>(new SnapshotRunService(new StubRunService(new RunDetails(run, [])), snapshot));
        Services.AddScoped<CircuitDataGate>();
        Services.AddSingleton<IRunUpdateFeed>(sp => new InProcessRunUpdateFeed(sp.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InProcessRunUpdateFeed>.Instance));
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        if (withOptions)
        {
            Services.AddOptions();
            Services.Configure<OrderOptions>(o => o.Revise = revise);
        }
        var auth = this.AddTestAuthorization().SetAuthorized(viewer);
        if (roles.Length > 0)
            auth.SetRoles(roles);
        return RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));
    }

    [Fact]
    public void A_direct_run_of_an_order_links_to_change_something_on_the_order_page()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On);

        var link = cut.Find("[data-testid='resume-change-link']");
        Assert.Equal("Change something", link.TextContent);
        Assert.Equal($"/orders/{_orderId}?change=1", link.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
    }

    [Theory]
    [InlineData(ReviseMode.Off, true)]
    [InlineData(ReviseMode.On, false)]
    public void Otherwise_resume_stays(ReviseMode revise, bool withOrder)
    {
        var cut = Render(SystemCrew.DirectTemplateName, withOrder ? _orderId : null, revise);

        Assert.Single(cut.FindAll("[data-testid='resume-run-button']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-change-link']"));
    }

    [Fact]
    public void Without_order_settings_resume_stays()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, withOptions: false);

        Assert.Single(cut.FindAll("[data-testid='resume-run-button']"));
    }

    [Fact]
    public void A_later_version_without_text_links_to_the_order_page_where_the_earlier_version_can_be_changed()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, finalText: null, version: 2);

        Assert.Single(cut.FindAll("[data-testid='resume-change-link']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
    }

    [Fact]
    public void A_first_version_without_text_keeps_resume_because_the_order_page_has_nothing_to_change()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, finalText: null, version: 1);

        Assert.Single(cut.FindAll("[data-testid='resume-run-button']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-change-link']"));
    }

    [Fact]
    public void An_administrator_on_someone_elses_run_keeps_resume_because_only_the_owner_can_change_the_text()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, viewer: "admin", roles: "admin");

        Assert.Single(cut.FindAll("[data-testid='resume-run-button']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-change-link']"));
    }

    private const string Shorter = "Make it shorter.";

    [Theory]
    [InlineData("admin", ReviseMode.On)]
    [InlineData("alice", ReviseMode.Off)]
    public void A_change_that_already_has_its_text_is_never_offered_to_resume_and_says_where_to_change_it(string viewer, ReviseMode revise)
    {
        var cut = viewer == "admin"
            ? Render(SystemCrew.DirectTemplateName, _orderId, revise, version: 2, viewer: viewer, revisionHint: Shorter, roles: "admin")
            : Render(SystemCrew.DirectTemplateName, _orderId, revise, version: 2, viewer: viewer, revisionHint: Shorter);

        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
        Assert.Equal(RevisionLines.ResumeChangedText, cut.Find("[data-testid='resume-refusal']").TextContent);
        Assert.Contains("“Change something”", RevisionLines.ResumeChangedText, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='delete-run-button']"));
    }

    [Fact]
    public void A_change_with_its_text_links_to_change_something_for_the_owner_without_a_second_sentence()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, version: 2, revisionHint: Shorter);

        Assert.Single(cut.FindAll("[data-testid='resume-change-link']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-refusal']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
    }

    [Fact]
    public void A_change_without_text_keeps_resume_for_an_administrator()
    {
        var cut = Render(SystemCrew.DirectTemplateName, _orderId, ReviseMode.On, finalText: null, version: 2, viewer: "admin", revisionHint: Shorter, roles: "admin");

        Assert.Single(cut.FindAll("[data-testid='resume-run-button']"));
        Assert.Empty(cut.FindAll("[data-testid='resume-refusal']"));
    }

    private sealed class SnapshotRunService(StubRunService inner, CrewSnapshot snapshot) : IRunService
    {

        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) =>
            inner.ListRunWarningsAsync(runId, requestingUsername, ct);

        public Task<RunEntity?> GetRunAsync(Guid id, string? user, CancellationToken ct = default) => inner.GetRunAsync(id, user, ct);

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken ct = default) =>
            inner.ListRunsAsync(limit, statusFilter, requestingUsername, ct);

        public Task<bool> CancelRunAsync(Guid id, string? user, CancellationToken ct = default) => inner.CancelRunAsync(id, user, ct);

        public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? user, CancellationToken ct = default) => inner.GetRunDetailsAsync(id, user, ct);

        public async Task<RunViewModel?> GetRunViewAsync(Guid id, string? user, CancellationToken ct = default) =>
            (await inner.GetRunViewAsync(id, user, ct))! with { Snapshot = snapshot };

        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? user, CancellationToken ct = default) => inner.ResumeRunAsync(options, user, ct);

        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => inner.DeleteRunAsync(runId, requestingUsername, ct);
    }
}
