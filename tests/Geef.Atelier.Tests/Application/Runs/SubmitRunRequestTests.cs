using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Application.Runs;

public sealed class SubmitRunRequestTests
{
    [Fact]
    public void RequiredFields_AreAccessible()
    {
        var request = new SubmitRunRequest("My briefing", "{}");

        Assert.Equal("My briefing", request.BriefingText);
        Assert.Equal("{}", request.ConfigJson);
    }

    [Fact]
    public void Attachments_DefaultsToNull()
    {
        var request = new SubmitRunRequest("briefing", "{}");

        Assert.Null(request.Attachments);
    }

    [Fact]
    public void CreatedByUser_DefaultsToNull()
    {
        var request = new SubmitRunRequest("briefing", "{}");

        Assert.Null(request.CreatedByUser);
    }

    [Fact]
    public void CrewTemplateName_DefaultsToNull()
    {
        var request = new SubmitRunRequest("briefing", "{}");

        Assert.Null(request.CrewTemplateName);
    }

    [Fact]
    public void CustomCrew_DefaultsToNull()
    {
        var request = new SubmitRunRequest("briefing", "{}");

        Assert.Null(request.CustomCrew);
    }

    [Fact]
    public void AllOptionalFields_CanBeSet()
    {
        var crew = new CrewSpec(
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["briefing-fidelity"],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null);

        var attachment = new RunAttachmentInput("notes.md", "text/markdown", Stream.Null);

        var request = new SubmitRunRequest(
            BriefingText: "briefing",
            ConfigJson: "{}",
            CreatedByUser: "alice",
            CrewTemplateName: "klassik",
            CustomCrew: crew,
            Attachments: [attachment]);

        Assert.Equal("alice", request.CreatedByUser);
        Assert.Equal("klassik", request.CrewTemplateName);
        Assert.Equal(crew, request.CustomCrew);
        Assert.Single(request.Attachments!);
        Assert.Equal("notes.md", request.Attachments![0].Filename);
    }
}
