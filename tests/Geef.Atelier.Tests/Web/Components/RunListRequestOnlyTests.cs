using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>Run cards and run rows show only the request of a composed briefing, never answers, assumptions or the profile.</summary>
public sealed class RunListRequestOnlyTests : TestContext
{
    private const string Request = "Kurzer Gruß ans Team.";

    private static RunEntity Run() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Completed,
        BriefingText = Request + BriefingSections.Separator + "Answers the person gave before writing:\n- Wer? → ANTWORT-MARKER-RL\n\n" +
                       BriefingSections.ProfileHeading + "\nName: PROFIL-MARKER-RL",
        ConfigJson = "{}",
        CreatedByUser = "alice",
    };

    [Fact]
    public void The_run_row_shows_only_the_request()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<RunRow>(p => p.Add(c => c.Run, Run()).Add(c => c.OnOpen, () => { }));

        Assert.Equal(Request, cut.Find(".title").TextContent.Trim());
    }
}
