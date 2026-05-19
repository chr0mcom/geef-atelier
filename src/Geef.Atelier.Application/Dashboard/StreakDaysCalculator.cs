namespace Geef.Atelier.Application.Dashboard;

/// <summary>
/// Counts how many consecutive calendar days (ending today) the user has completed at least one run.
/// Dates must be UTC dates; the sequence need not be contiguous in the input list.
/// </summary>
public static class StreakDaysCalculator
{
    public static int Calculate(IEnumerable<DateOnly> runDates, DateOnly today)
    {
        var dates = new HashSet<DateOnly>(runDates);
        if (!dates.Contains(today)) return 0;

        var streak = 0;
        var current = today;
        while (dates.Contains(current))
        {
            streak++;
            current = current.AddDays(-1);
        }
        return streak;
    }
}
