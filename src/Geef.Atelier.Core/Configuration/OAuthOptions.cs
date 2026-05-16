namespace Geef.Atelier.Core.Configuration;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    public string Issuer { get; set; } = "https://geef.stefan-bechtel.de";
    /// <summary>Bearer token required to call POST /oauth/register. If empty, registration is open.</summary>
    public string RegistrationToken { get; set; } = "";
    public int AuthorizationCodeTtlMinutes { get; set; } = 10;
    public int AccessTokenTtlHours { get; set; } = 1;
    public int RefreshTokenTtlDays { get; set; } = 30;
    /// <summary>Interval in minutes for the cleanup background service.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;
}
