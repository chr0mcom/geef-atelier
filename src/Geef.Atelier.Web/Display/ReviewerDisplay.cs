using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Web.Display;

/// <summary>Display names of the checks and setups on the technical run page.</summary>
public static class ReviewerDisplay
{
    /// <summary>Display name of the check <paramref name="reviewerName"/>; an unknown check shows its stored name.</summary>
    public static string ToDisplay(string reviewerName) =>
        SystemCrew.CodeOnlyReviewerProfiles.TryGetValue(reviewerName, out var codeOnly) ? codeOnly.DisplayName : reviewerName;

    /// <summary>Display name of the setup <paramref name="templateName"/>; an unknown setup shows its stored name.</summary>
    public static string GetTemplateDisplay(string templateName) =>
        SystemCrew.CodeOnlyTemplates.TryGetValue(templateName, out var codeOnly) ? codeOnly.DisplayName : templateName;
}
