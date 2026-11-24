using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>Names of profiles as shown to the user in warnings.</summary>
internal static class ProfileDisplayNameExtensions
{
    /// <summary>Display name of the profile, or its technical name when no display name is set.</summary>
    public static string DisplayNameOrName(this GroundingProviderProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Name : profile.DisplayName;

    /// <summary>Display name of the profile, or its technical name when no display name is set.</summary>
    public static string DisplayNameOrName(this FinalizerProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Name : profile.DisplayName;

    /// <summary>Warning text for a source that failed and was left out; names the source by its display name only.</summary>
    public static string SkippedSourceWarning(this GroundingProviderProfile profile) =>
        $"The source \"{profile.DisplayNameOrName()}\" could not be read and was skipped.";

    /// <summary>Warning text for a knowledge source whose search setting is unknown; names the source by its display name only.</summary>
    public static string UnknownScopeWarning(this GroundingProviderProfile profile) =>
        $"The source \"{profile.DisplayNameOrName()}\" has an unknown search setting, so only shared documents were searched.";
}
