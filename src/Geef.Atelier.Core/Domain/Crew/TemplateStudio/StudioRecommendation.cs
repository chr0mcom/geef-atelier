namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>How the Template Studio recommends to proceed based on the meta-LLM analysis.</summary>
public enum StudioRecommendation
{
    UseExistingTemplate,
    CreateNewTemplate,
    AdaptExistingTemplate
}
