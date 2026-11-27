using System.Diagnostics;

namespace Geef.Atelier.Tests;

/// <summary>
/// Guards against text processing that grows faster than its input (backtracking patterns, nested scans) without a limit that depends on
/// the speed or the load of the machine: the same work is timed on an input and on one <see cref="Factor"/> times as large. Linear work
/// takes about <see cref="Factor"/> times as long, quadratic work about <see cref="Factor"/> squared (16), so
/// <see cref="MaxRatio"/> lies well between the two and leaves room for caches and garbage collection. Each size counts with its fastest
/// run, because load only ever makes a run slower. The two sizes are timed in turns, so that a busy phase of the machine hits both. When
/// the ratio after <see cref="MinRounds"/> rounds is not below the limit, further rounds are timed, up to <see cref="MaxRounds"/> and
/// within <see cref="ExtraRoundsBudget"/>: this gives each size more chances for an undisturbed run and leaves the limit as it is, so
/// work that really grows too fast stays above it. Times below <see cref="Floor"/> count as the floor, so that timer noise on very short
/// runs never fails the test; the input has to be large enough that the large run is clearly above it. The generous
/// <see cref="HangLimit"/> only stops work that does not end in any reasonable time.
/// </summary>
internal static class ScalingAssert
{
    private const int Factor = 4;
    private const int MinRounds = 3;
    private const int MaxRounds = 12;
    private const double MaxRatio = 10;
    private static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan HangLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExtraRoundsBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <paramref name="prepare"/> builds, for a size in percent of the large input (25 or 100), the work to be timed; building the input is
    /// not timed.
    /// </summary>
    public static void GrowsAboutLinearly(Func<int, Action> prepare)
    {
        var small = prepare(100 / Factor);
        var large = prepare(100);
        small();

        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;
        var ratio = double.MaxValue;
        var total = Stopwatch.StartNew();
        for (var round = 1; round <= MaxRounds; round++)
        {
            smallTime = Faster(smallTime, small);
            largeTime = Faster(largeTime, large);
            ratio = largeTime / (smallTime > Floor ? smallTime : Floor);
            if (round >= MinRounds && (ratio < MaxRatio || total.Elapsed > ExtraRoundsBudget))
                break;
        }

        Assert.True(ratio < MaxRatio,
            $"{Factor} times the input took {ratio:F1} times as long ({smallTime.TotalMilliseconds:F0} ms, then {largeTime.TotalMilliseconds:F0} ms).");
    }

    private static TimeSpan Faster(TimeSpan fastest, Action work)
    {
        var watch = Stopwatch.StartNew();
        work();
        watch.Stop();
        Assert.True(watch.Elapsed < HangLimit, $"One run took {watch.Elapsed}.");
        return watch.Elapsed < fastest ? watch.Elapsed : fastest;
    }
}
