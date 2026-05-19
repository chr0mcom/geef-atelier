using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Application.Dashboard;

/// <summary>Derives a <see cref="TrendDirection"/> from a percentage change value.</summary>
public static class TrendFormatter
{
    /// <summary>
    /// Changes within ±5 % are considered flat.
    /// </summary>
    public static TrendDirection GetDirection(decimal pct)
        => pct > 5m ? TrendDirection.Up
         : pct < -5m ? TrendDirection.Down
         : TrendDirection.Flat;

    /// <summary>Computes percentage change from <paramref name="previous"/> to <paramref name="current"/>.</summary>
    public static decimal ComputePct(decimal current, decimal previous)
    {
        if (previous == 0) return current > 0 ? 100m : 0m;
        return (current - previous) / Math.Abs(previous) * 100m;
    }
}
