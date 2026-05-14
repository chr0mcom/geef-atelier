namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>Confidence match of an existing template against the user's task description.</summary>
public sealed record TemplateMatch(
    string TemplateName,
    double Confidence,
    string Reasoning);
