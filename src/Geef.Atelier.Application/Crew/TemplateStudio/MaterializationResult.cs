namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Result of materializing (persisting) a Template Studio proposal.</summary>
public sealed record MaterializationResult(
    string CreatedTemplateName,
    IReadOnlyList<string> CreatedProfileNames,
    IReadOnlyList<string> Warnings);
