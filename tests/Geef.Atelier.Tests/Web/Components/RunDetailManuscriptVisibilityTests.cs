using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application_IKnowledgeService = Geef.Atelier.Application.Crew.Knowledge.IKnowledgeService;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class RunDetailManuscriptVisibilityTests : TestContext
{
    private readonly Guid _runId = Guid.NewGuid();

    private void RegisterServices(RunStatus status, string? finalText, string? errorMessage = null)
    {
        var details = new RunDetails(
            new RunEntity
            {
                Id = _runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = status,
                BriefingText = "Test briefing",
                ConfigJson = "{}",
                FinalText = finalText,
                ErrorMessage = errorMessage,
            },
            []);

        Services.AddSingleton<IRunService>(new StubRunService(details));
        Services.AddSingleton<IRunArtifactRepository>(new StubArtifactRepository([]));
        Services.AddSingleton<Application_IKnowledgeService>(new NoOpKnowledgeService());
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
    }

    [Fact]
    public void FailedRunWithFinalText_RendersManuscriptAndResumeButton()
    {
        RegisterServices(RunStatus.Failed, "# Best effort\n\nDraft.",
            "Pipeline stopped: maximum iterations reached");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.NotEmpty(cut.FindAll(".manuscript-wrap"));
        cut.Find("[data-testid='resume-run-button']");
    }

    [Fact]
    public void FailedRunWithoutFinalText_DoesNotRenderManuscript()
    {
        RegisterServices(RunStatus.Failed, finalText: null,
            "Pipeline stopped: maximum iterations reached");

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Empty(cut.FindAll(".manuscript-wrap"));
    }

    [Fact]
    public void AbortedRunWithFinalText_RendersManuscript()
    {
        RegisterServices(RunStatus.Aborted, "# Best effort\n\nDraft.",
            "Aborted due to critical reviewer finding");

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
}
