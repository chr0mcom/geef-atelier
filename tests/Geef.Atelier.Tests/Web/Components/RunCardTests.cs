using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class RunCardTests : TestContext
{
    private static RunEntity MakeRun(string briefing, RunStatus status = RunStatus.Pending) =>
        new()
        {
            Id           = Guid.NewGuid(),
            CreatedAt    = DateTimeOffset.UtcNow,
            Status       = status,
            BriefingText = briefing,
            ConfigJson   = "{}",
            TokensTotal  = 0
        };

    [Fact]
    public void ShortBriefing_ShowsFullText()
    {
        var run = MakeRun("Hello world");
        var cut = RenderComponent<RunCard>(p => p.Add(c => c.Run, run));

        Assert.Contains("Hello world", cut.Markup);
        Assert.DoesNotContain("…", cut.Markup);
    }

    [Fact]
    public void LongBriefing_ShowsEllipsedSnippet()
    {
        var longBriefing = new string('x', 70);
        var run = MakeRun(longBriefing);
        var cut = RenderComponent<RunCard>(p => p.Add(c => c.Run, run));

        Assert.Contains("…", cut.Markup);
        Assert.DoesNotContain(longBriefing, cut.Markup);
    }

    [Fact]
    public void ExactlyAtLimit_ShowsFullText()
    {
        var briefing = new string('y', 60);
        var run = MakeRun(briefing);
        var cut = RenderComponent<RunCard>(p => p.Add(c => c.Run, run));

        Assert.DoesNotContain("…", cut.Markup);
    }

    [Fact]
    public void OpensLinkToRunDetail()
    {
        var run = MakeRun("test");
        var cut = RenderComponent<RunCard>(p => p.Add(c => c.Run, run));

        var link = cut.Find("a.run-card-link");
        Assert.Contains($"/runs/{run.Id}", link.GetAttribute("href"));
    }
}
