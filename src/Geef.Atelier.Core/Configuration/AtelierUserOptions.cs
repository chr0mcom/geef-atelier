namespace Geef.Atelier.Core.Configuration;

/// <summary>Configuration options for the single admin user account.</summary>
public sealed class AtelierUserOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AtelierUser";

    /// <summary>The admin username.</summary>
    public string Username { get; set; } = "";

    /// <summary>BCrypt hash of the admin password.</summary>
    public string PasswordHash { get; set; } = "";
}
