using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Web.Display;

/// <summary>Conservative heuristic mapping run status to an active pipeline stage index (0=Draft, 1=Review, 2=Revision).</summary>
public static class PressStageMapper
{
    public static int ActiveStage(RunStatus status) => status switch
    {
        RunStatus.Running => 0,
        _                 => -1,
    };

    public static int InkPercent1(RunStatus status) => status == RunStatus.Completed ? 100 : 0;
    public static int InkPercent2(RunStatus status) => status == RunStatus.Completed ? 100 : 0;
}
