namespace Geef.Atelier.Web.Display;

public static class ReviewerDisplay
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        // PS-5 slug-based names (canonical)
        ["briefing-fidelity"] = "BriefingFidelity",
        ["clarity"]           = "Clarity",
        // Pre-PS-5 class-name-based names (historical, defensive fallback)
        ["BriefingTreueReviewer"] = "BriefingFidelity",
        ["KlarheitReviewer"]      = "Clarity",
    };

    public static string ToDisplay(string reviewerName) =>
        DisplayNames.TryGetValue(reviewerName, out var display) ? display : reviewerName;
}
