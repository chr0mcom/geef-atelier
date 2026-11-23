using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Web.Display;

/// <summary>Display helpers that tell the user when a manually chosen binding is rewritten on save (D-066).</summary>
public static class CliRouteNoticeExtensions
{
    /// <summary>
    /// Returns the hint text when <see cref="CliRoute.Normalize"/> would save a different provider or model than the
    /// one chosen; <c>null</c> when the binding is kept as is or incomplete.
    /// </summary>
    public static string? DescribeCliRouteRewrite(this ModelCandidate chosen)
    {
        if (!CliRoute.IsNonCliRoute(chosen.Provider, chosen.Model))
            return null;

        var target = CliRoute.Normalize(chosen.Provider, chosen.Model);
        return $"This model will be saved as {target.Provider}/{target.Model} (CLI route / retired model).";
    }
}
