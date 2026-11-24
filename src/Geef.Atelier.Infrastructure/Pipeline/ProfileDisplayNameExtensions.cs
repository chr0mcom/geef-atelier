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
}
