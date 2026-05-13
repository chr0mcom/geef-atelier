using Geef.Atelier.Core.Domain.Crew;

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

    /// <summary>
    /// Returns the display name for an executor profile.
    /// Looks up <see cref="SystemCrew.ExecutorProfiles"/> first; falls back to
    /// <paramref name="fallbackDisplayName"/> or <paramref name="profileName"/>.
    /// </summary>
    public static string GetExecutorDisplay(string profileName, string? fallbackDisplayName = null) =>
        SystemCrew.ExecutorProfiles.TryGetValue(profileName, out var profile)
            ? profile.DisplayName
            : fallbackDisplayName ?? profileName;

    /// <summary>
    /// Returns the display name for a crew template.
    /// Looks up <see cref="SystemCrew.CrewTemplates"/> first; falls back to <paramref name="templateName"/>.
    /// </summary>
    public static string GetTemplateDisplay(string templateName) =>
        SystemCrew.CrewTemplates.TryGetValue(templateName, out var template)
            ? template.DisplayName
            : templateName;

    /// <summary>Returns a human-readable label for an <see cref="EvaluationStrategy"/> value.</summary>
    public static string GetEvaluationStrategyDisplay(EvaluationStrategy strategy) =>
        strategy switch
        {
            EvaluationStrategy.Parallel   => "Parallel",
            EvaluationStrategy.Sequential => "Sequential",
            EvaluationStrategy.FailFast   => "FailFast",
            EvaluationStrategy.Priority   => "Priority",
            _                             => strategy.ToString()
        };
}
