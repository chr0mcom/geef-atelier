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
