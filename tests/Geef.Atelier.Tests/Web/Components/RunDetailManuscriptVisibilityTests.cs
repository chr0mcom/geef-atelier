using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class RunDetailManuscriptVisibilityTests : TestContext
{
    private readonly Guid _runId = Guid.NewGuid();

    private void RegisterServices(RunStatus status, string? finalText, string? errorMessage = null, bool withSnapshot = true)
    {
        var details = new RunDetails(
            new RunEntity
            {
                Id = _runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = status,
                BriefingText = "Test briefing",
                FinalText = finalText,
                ErrorMessage = errorMessage,
                CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
                CrewSnapshot = withSnapshot ? TestCrewSnapshots.RunnableJson : null,
            },
            []);

        Services.AddSingleton<IRunService>(new StubRunService(details));
        Services.AddScoped<Geef.Atelier.Web.Components.CircuitDataGate>();
        Services.AddSingleton<Geef.Atelier.Web.Notifications.IRunUpdateFeed>(sp => new Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed>.Instance));
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
    }

    [Fact]
    public void FailedRunWithFinalText_RendersManuscriptAndResumeButton()
    {
        RegisterServices(RunStatus.Failed, "# Best effort\n\nDraft.",
            "The text still had open points after the maximum number of rounds.");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.NotEmpty(cut.FindAll(".manuscript-wrap"));
        cut.Find("[data-testid='resume-run-button']");
    }

    [Fact]
    public void FailedRunWithoutFinalText_DoesNotRenderManuscript()
    {
        RegisterServices(RunStatus.Failed, finalText: null,
            "The text still had open points after the maximum number of rounds.");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Empty(cut.FindAll(".manuscript-wrap"));
    }

    [Fact]
    public void AbortedRunWithFinalText_RendersManuscript()
    {
        RegisterServices(RunStatus.Aborted, "# Best effort\n\nDraft.",
            "A check found a serious problem, so the work was stopped.");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.NotEmpty(cut.FindAll(".manuscript-wrap"));
        cut.Find("[data-testid='resume-run-button']");
    }

    [Fact]
    public void CompletedRunWithFinalText_StillRendersManuscript()
    {
        RegisterServices(RunStatus.Completed, "# Final\n\nContent.");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.NotEmpty(cut.FindAll(".manuscript-wrap"));
        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
    }

    [Theory]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Aborted)]
    public void TaskWithoutUsableSetup_OffersNoResumeButStillDelete(RunStatus status)
    {
        RegisterServices(status, finalText: null, "The attached files could not be prepared. Please submit the task again.", withSnapshot: false);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Empty(cut.FindAll("[data-testid='resume-run-button']"));
        cut.Find("[data-testid='delete-run-button']");
    }
}
