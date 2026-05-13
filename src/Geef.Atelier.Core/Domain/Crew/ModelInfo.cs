namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>Describes a single model available from an LLM provider.</summary>
public sealed record ModelInfo(
    string Id,
    string DisplayName,
    string? Description,
    bool IsRecommended)
{
    /// <summary>Returns <see cref="Id"/> when <see cref="DisplayName"/> is missing.</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
