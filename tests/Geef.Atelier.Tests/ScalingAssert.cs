using System.Diagnostics;

namespace Geef.Atelier.Tests;

/// <summary>
/// Guards against text processing that grows faster than its input (backtracking patterns, nested scans) without a limit that depends on
/// the speed or the load of the machine: the same work is timed on an input and on one <see cref="Factor"/> times as large. Linear work
/// takes about <see cref="Factor"/> times as long, quadratic work about <see cref="Factor"/> squared (16), so
/// <see cref="MaxRatio"/> lies well between the two and leaves room for caches and garbage collection. Each size counts with its fastest
/// of <see cref="Runs"/> runs, because load only ever makes a run slower. Times below <see cref="Floor"/> count as the floor, so that
/// timer noise on very short runs never fails the test; the input has to be large enough that the large run is clearly above it. The
/// generous <see cref="HangLimit"/> only stops work that does not end in any reasonable time.
/// </summary>
internal static class ScalingAssert
{
    private const int Factor = 4;
    private const int Runs = 3;
    private const double MaxRatio = 10;
    private static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan HangLimit = TimeSpan.FromMinutes(2);

    /// <summary>
    /// <paramref name="prepare"/> builds, for a size in percent of the large input (25 or 100), the work to be timed; building the input is
    /// not timed.
    /// </summary>
    public static void GrowsAboutLinearly(Func<int, Action> prepare)
    {
        var small = prepare(100 / Factor);
        var large = prepare(100);
        small();

        var smallTime = Fastest(small);
        var largeTime = Fastest(large);

        var ratio = largeTime / (smallTime > Floor ? smallTime : Floor);
        Assert.True(ratio < MaxRatio,
            $"{Factor} times the input took {ratio:F1} times as long ({smallTime.TotalMilliseconds:F0} ms, then {largeTime.TotalMilliseconds:F0} ms).");
    }

    private static TimeSpan Fastest(Action work)
    {
        var fastest = TimeSpan.MaxValue;
        for (var run = 0; run < Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            work();
            watch.Stop();
            Assert.True(watch.Elapsed < HangLimit, $"One run took {watch.Elapsed}.");
            if (watch.Elapsed < fastest)
                fastest = watch.Elapsed;
        }
        return fastest;
    }
}
