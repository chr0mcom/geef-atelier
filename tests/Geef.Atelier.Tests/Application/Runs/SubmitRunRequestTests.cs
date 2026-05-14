using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Application.Runs;

public sealed class SubmitRunRequestTests
{
    [Fact]
    public void AllOptionalFields_CanBeSet()
    {
        var crew = new CrewSpec(
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["briefing-fidelity"],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null);

        var attachment = new RunAttachmentInput("notes.md", "text/markdown", []);

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

    [Fact]
    public void Attachments_ContainsAllSuppliedItems()
    {
        var a1 = new RunAttachmentInput("doc1.txt", "text/plain", "hello"u8.ToArray());
        var a2 = new RunAttachmentInput("doc2.md", "text/markdown", "world"u8.ToArray());

        var request = new SubmitRunRequest(
            BriefingText: "briefing",
            ConfigJson: "{}",
            Attachments: [a1, a2]);

        Assert.Equal(2, request.Attachments!.Count);
        Assert.Equal("doc1.txt", request.Attachments[0].Filename);
        Assert.Equal("doc2.md", request.Attachments[1].Filename);
    }

    [Fact]
    public void EmptyBriefingText_CanBeConstructed()
    {
        // Validation is enforced at the application-service boundary, not in the record itself.
        var request = new SubmitRunRequest(BriefingText: "", ConfigJson: "{}");

        Assert.Equal(string.Empty, request.BriefingText);
    }
}
