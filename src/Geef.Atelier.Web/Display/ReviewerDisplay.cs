using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Web.Display;

/// <summary>Display names of the checks and setups on the technical run page.</summary>
public static class ReviewerDisplay
{
    private static readonly Dictionary<string, string> RetiredDisplayNames = new(StringComparer.Ordinal)
    {
        ["briefing-fidelity"] = "BriefingFidelity",
        ["clarity"]           = "Clarity",
        ["BriefingTreueReviewer"] = "BriefingFidelity",
        ["KlarheitReviewer"]      = "Clarity",
    };

    /// <summary>Display name of the check <paramref name="reviewerName"/>; checks of the removed review loop keep their old short names.</summary>
    public static string ToDisplay(string reviewerName) =>
        SystemCrew.CodeOnlyReviewerProfiles.TryGetValue(reviewerName, out var codeOnly) ? codeOnly.DisplayName
        : RetiredDisplayNames.TryGetValue(reviewerName, out var display) ? display
        : reviewerName;

    /// <summary>Display name of the setup <paramref name="templateName"/>, or the name itself for a setup this version no longer has.</summary>
    public static string GetTemplateDisplay(string templateName) =>
        SystemCrew.CodeOnlyTemplates.TryGetValue(templateName, out var codeOnly) ? codeOnly.DisplayName : templateName;
}
