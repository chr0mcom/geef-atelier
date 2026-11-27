using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>The run page shows the "About me" block of the briefing only to the owner; an administrator viewing a foreign run sees a note instead.</summary>
public sealed class RunDetailProfileVisibilityTests : TestContext
{
    private const string Marker = "PROFILMARKER Kiefernweg";
    private readonly Guid _runId = Guid.NewGuid();

    private static readonly string Briefing =
        "Schreib eine Einladung." + BriefingSections.Separator + IntakeBriefComposer.ProfileHeading + "\nAnything else: " + Marker;

    private IRenderedComponent<RunDetail> Render(string viewer, bool admin)
    {
        var details = new RunDetails(new RunEntity
        {
            Id = _runId, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Completed, BriefingText = Briefing, ConfigJson = "{}",
            FinalText = "Text.", CreatedByUser = "alice",
        }, []);
        Services.AddSingleton<IRunService>(new StubRunService(details));
        Services.AddScoped<Geef.Atelier.Web.Components.CircuitDataGate>();
        Services.AddSingleton<Geef.Atelier.Web.Notifications.IRunUpdateFeed>(sp => new Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed>.Instance));
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(viewer);
        if (admin)
            auth.SetRoles(Geef.Atelier.Application.Auth.OwnerScope.AdminRole);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));
    }

    [Fact]
    public void The_owner_sees_the_whole_briefing()
    {
        var cut = Render("alice", admin: false);

        Assert.Contains(Marker, cut.Find("[data-testid='run-briefing']").TextContent);
    }

    [Fact]
    public void An_administrator_viewing_a_foreign_run_does_not_see_the_profile()
    {
        var cut = Render("admin", admin: true);

        Assert.DoesNotContain(Marker, cut.Markup);
        Assert.Contains(BriefingSections.ProfileHiddenNote, cut.Find("[data-testid='run-briefing']").TextContent);
        Assert.Contains("Schreib eine Einladung.", cut.Find("[data-testid='run-briefing']").TextContent);
    }

    [Theory]
    [InlineData("alice", false)]
    [InlineData("admin", true)]
    public void The_heading_shows_only_the_request(string viewer, bool admin)
    {
        var cut = Render(viewer, admin);

        Assert.Equal("Schreib eine Einladung.", cut.Find("h1").TextContent.Trim());
    }

}
