namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Model vendor family, used for cross-family reviewer ordering and the CLI-route rule.</summary>
public enum VendorFamily
{
    /// <summary>Any other vendor.</summary>
    Other = 0,

    /// <summary>Anthropic Claude models.</summary>
    Anthropic = 1,

    /// <summary>OpenAI GPT models.</summary>
    OpenAi = 2,

    /// <summary>Google Gemini models.</summary>
    Google = 3,
}
