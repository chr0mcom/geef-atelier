namespace Geef.Atelier.Application.Dashboard;

/// <summary>
/// Computes the craft-mark score (0–100) from a run history.
/// Score = (completedRuns / totalRuns) * 100, clamped to [0, 100].
/// Returns 0 when totalRuns is 0.
/// </summary>
public static class CraftMarkCalculator
{
    public static double Calculate(int completedRuns, int totalRuns)
    {
        if (totalRuns <= 0) return 0.0;
        var raw = (double)completedRuns / totalRuns * 100.0;
        return Math.Clamp(raw, 0.0, 100.0);
    }
}
