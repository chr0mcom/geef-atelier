using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>Which versions of an order the list of versions shows and compares; the same for the list and for the link above the text.</summary>
public static class OrderVersionList
{
    /// <summary>The listed versions: the newest run of each version number, newest version first.</summary>
    public static IReadOnlyList<OrderRunView> Listed(this IReadOnlyList<OrderRunView> runs) => runs
        .Where(r => r.Version is not null)
        .GroupBy(r => r.Version!.Value)
        .Select(g => g.OrderByDescending(r => r.CreatedAt).First())
        .OrderByDescending(r => r.Version)
        .ToList();

    /// <summary>True when the version has a text: its words were counted, or it is the version shown above (<paramref name="shownVersion"/>).</summary>
    public static bool HasText(this OrderRunView run, int? shownVersion) => run.WordCount is > 0 || run.Version == shownVersion;

    /// <summary>
    /// The version that “Show changes” of <paramref name="version"/> compares with: the newest earlier version with a text. <c>null</c> when
    /// there is none, and the list then offers no “Show changes” for that version.
    /// </summary>
    public static int? ComparedWith(this IReadOnlyList<OrderRunView> runs, int version, int? shownVersion) =>
        runs.Listed().Where(r => r.Version < version && r.HasText(shownVersion)).Select(r => r.Version).FirstOrDefault();
}
