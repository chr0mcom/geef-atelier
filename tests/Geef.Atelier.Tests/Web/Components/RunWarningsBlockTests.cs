using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>A9 (Etappe 0 review): the introduction of the warnings block matches the state of the task.</summary>
public sealed class RunWarningsBlockTests : TestContext
{
    [Theory]
    [InlineData(RunStatus.Running, false, "still working")]
    [InlineData(RunStatus.Pending, false, "still working")]
    [InlineData(RunStatus.Completed, true, "The result is ready")]
    [InlineData(RunStatus.Failed, true, "The result is ready")]
    [InlineData(RunStatus.Failed, false, "ended without a result")]
    [InlineData(RunStatus.Aborted, false, "ended without a result")]
    public void IntroductionFitsTheState(RunStatus status, bool hasText, string expected)
    {
        var cut = RenderComponent<RunWarningsBlock>(p => p
            .Add(c => c.Warnings, [Warning()])
            .Add(c => c.Status, status)
            .Add(c => c.HasText, hasText));

        var intro = cut.Find("[data-testid='run-warnings-intro']").TextContent;
        Assert.Contains(expected, intro);
        if (status is RunStatus.Running or RunStatus.Pending || !hasText)
            Assert.DoesNotContain("finished", intro);
    }

    [Theory]
    [InlineData(RunStatus.Running, "Version 3 is still being written.", "This does not concern the text above (version 2):")]
    [InlineData(RunStatus.Pending, "Version 3 is still being written.", "This does not concern the text above (version 2):")]
    [InlineData(RunStatus.Failed, "Version 3 could not be finished.", "They do not concern the text above (version 2), which is complete and unchanged:")]
    [InlineData(RunStatus.Aborted, "Version 3 could not be finished.", "They do not concern the text above (version 2), which is complete and unchanged:")]
    public void Warnings_of_a_newer_version_than_the_text_shown_name_both_versions(RunStatus status, string start, string end)
    {
        var cut = RenderComponent<RunWarningsBlock>(p => p
            .Add(c => c.Warnings, [Warning()])
            .Add(c => c.Status, status)
            .Add(c => c.HasText, false)
            .Add(c => c.Version, 3)
            .Add(c => c.ShownVersion, 2));

        var intro = cut.Find("[data-testid='run-warnings-intro']").TextContent;
        Assert.StartsWith(start, intro);
        Assert.EndsWith(end, intro);
        Assert.DoesNotContain("without a result", intro);
        Assert.Equal("Warnings for version 3", cut.Find("h2").TextContent);
        Assert.Equal("run-warnings-title-v3", cut.Find("h2").Id);
        Assert.Equal("run-warnings-title-v3", cut.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void The_same_version_as_the_text_shown_keeps_the_usual_introduction()
    {
        var cut = RenderComponent<RunWarningsBlock>(p => p
            .Add(c => c.Warnings, [Warning()])
            .Add(c => c.Status, RunStatus.Completed)
            .Add(c => c.HasText, true)
            .Add(c => c.Version, 2)
            .Add(c => c.ShownVersion, 2));

        Assert.StartsWith("The result is ready", cut.Find("[data-testid='run-warnings-intro']").TextContent);
        Assert.Equal("Warnings for version 2", cut.Find("h2").TextContent);
    }

    private static RunWarningEntity Warning() => new()
    {
        Id = Guid.NewGuid(),
        RunId = Guid.NewGuid(),
        Source = RunWarningSources.Grounding,
        Code = RunWarningCodes.GroundingProviderSkipped,
        Message = "The source \"Web search\" could not be read and was skipped.",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
