namespace Geef.Atelier.Core.Domain.Profiles;

/// <summary>Renders the profile for the clarifier and the writer.</summary>
public static class ProfilePrompt
{
    /// <summary>True when there is no profile or none of its fields is set.</summary>
    public static bool IsEmpty(UserProfileEntity? profile) => throw new NotImplementedException();

    /// <summary>Plain block "About the person" for clarifier and writer; null when empty. Fields in fixed order, labels in English.</summary>
    public static string? Render(UserProfileEntity? profile) => throw new NotImplementedException();
}
