using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

/// <summary>Rules a crew template must meet before it is saved.</summary>
public static class CrewTemplateRules
{
    /// <summary>Shown when a template is saved without any reviewer; a run from it could never be checked.</summary>
    public const string NoReviewerMessage = "Please choose at least one reviewer. Without a reviewer the text cannot be checked.";

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with <see cref="NoReviewerMessage"/> when
    /// <paramref name="template"/> names no reviewer.
    /// </summary>
    public static void EnsureHasReviewer(this CrewTemplate template)
    {
        if (template.ReviewerProfileNames is not { Count: > 0 } names || names.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(NoReviewerMessage);
    }
}
