namespace Geef.Atelier.Core.Scheduling;

/// <summary>Computes the delay until the next occurrence of a daily UTC wall-clock time.</summary>
public static class NightlyScheduleCalculator
{
    /// <summary>
    /// Returns the time span from <paramref name="nowUtc"/> until the next occurrence of
    /// <paramref name="hourUtc"/>:<paramref name="minuteUtc"/> UTC. When the target time has already
    /// passed today — or falls exactly on <paramref name="nowUtc"/> — the next day's occurrence is
    /// used, so the result is always greater than zero and never exceeds 24 hours.
    /// Out-of-range hours and minutes are clamped to [0, 23] and [0, 59].
    /// </summary>
    public static TimeSpan DelayUntilNext(DateTimeOffset nowUtc, int hourUtc, int minuteUtc)
    {
        var hour = Math.Clamp(hourUtc, 0, 23);
        var minute = Math.Clamp(minuteUtc, 0, 59);

        var utc = nowUtc.ToUniversalTime();
        var target = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour, minute, 0, TimeSpan.Zero);

        if (target <= utc)
            target = target.AddDays(1);

        return target - utc;
    }
}
