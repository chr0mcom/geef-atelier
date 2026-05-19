namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>Discriminator for a profile proposed by the Template Studio meta-LLM.</summary>
public enum ProposedProfileType
{
    Reviewer,
    Executor,
    Advisor,
    GroundingProvider,
    Finalizer
}
