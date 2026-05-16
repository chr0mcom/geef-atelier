namespace Geef.Atelier.Core.Configuration;

public sealed class AtelierMcpOptions
{
    public const string SectionName = "AtelierMcp";
    public string Token { get; set; } = "";

    /// <summary>
    /// The username attributed to runs submitted via the static bearer token.
    /// Defaults to <see cref="AtelierUserOptions.Username"/> when not set.
    /// </summary>
    public string? StaticTokenUser { get; set; }
}
