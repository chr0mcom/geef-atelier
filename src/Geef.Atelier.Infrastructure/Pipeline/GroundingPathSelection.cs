using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>Decides whether a grounding profile runs through the tool path or the provider-type path.</summary>
internal static class GroundingPathSelection
{
    /// <summary>
    /// System profiles whose tool binding is broken (knowledge-base and academic-search are not wired in the tool
    /// executor, and web-search read a key name the container does not set). They always run through the
    /// provider-type path until the tool path is removed.
    /// </summary>
    public static readonly IReadOnlySet<string> ProviderTypePathSystemProfiles = new HashSet<string>(StringComparer.Ordinal)
    {
        SystemCrew.TavilyRefinedProfile.Name,
        SystemCrew.TavilyBasicProfile.Name,
        SystemCrew.RunAttachmentsProfile.Name,
        SystemCrew.KnowledgeBaseDefaultProfile.Name,
        SystemCrew.AcademicDefaultProfile.Name,
    };

    /// <summary>True when <paramref name="profile"/> is executed by the tool-backed provider.</summary>
    public static bool UsesToolPath(this GroundingProviderProfile profile) =>
        profile.ToolName is { Length: > 0 }
        && !(profile.IsSystem && ProviderTypePathSystemProfiles.Contains(profile.Name));
}
