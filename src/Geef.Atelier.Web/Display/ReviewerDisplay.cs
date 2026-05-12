namespace Geef.Atelier.Web.Display;

public static class ReviewerDisplay
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        ["BriefingTreueReviewer"] = "BriefingFidelity",
        ["KlarheitReviewer"]      = "Clarity",
    };

    public static string ToDisplay(string reviewerName) =>
        DisplayNames.TryGetValue(reviewerName, out var display) ? display : reviewerName;
}
