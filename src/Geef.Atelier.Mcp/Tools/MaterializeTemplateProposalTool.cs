using System.ComponentModel;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class MaterializeTemplateProposalTool
{
    [McpServerTool, Description(
        "Persists a Template Studio proposal as custom crew records (template + profiles). " +
        "Requires the analysisId from analyze_template_proposal. " +
        "Accepts user-edited or direct proposal output as finalTemplate and finalNewProfiles. " +
        "Returns the name of the created template and any provider-availability warnings.")]
    public static async Task<MaterializeTemplateProposalOutput> MaterializeTemplateProposal(
        ITemplateStudioService studioService,
        [Description("The analysis ID returned by analyze_template_proposal.")] Guid analysisId,
        [Description("The template to persist. Can be the proposedTemplate from analyze output, optionally user-edited.")] ProposedTemplateDto finalTemplate,
        [Description("New profiles to create. Can be proposedNewProfiles from analyze output, optionally user-edited. Pass empty array if no new profiles needed.")] IReadOnlyList<ProposedProfileDto> finalNewProfiles,
        CancellationToken cancellationToken = default)
    {
        var request = new MaterializationRequest(
            MapTemplate(finalTemplate),
            finalNewProfiles.Select(MapProfile).ToList());

        var result = await studioService.MaterializeAsync(analysisId, request, cancellationToken);

        return new MaterializeTemplateProposalOutput(
            result.CreatedTemplateName,
            result.CreatedProfileNames,
            result.Warnings);
    }

    private static ProposedTemplate MapTemplate(ProposedTemplateDto dto) => new(
        dto.Name, dto.DisplayName, dto.Description, dto.ExecutorProfileName,
        dto.ReviewerProfileNames, dto.AdvisorProfileNames, dto.GroundingProviderProfileNames,
        dto.EvaluationStrategy);

    private static ProposedProfile MapProfile(ProposedProfileDto dto)
    {
        if (!Enum.TryParse<ProposedProfileType>(dto.ProfileType, ignoreCase: true, out var profileType))
            throw new ArgumentException($"Unknown ProfileType: '{dto.ProfileType}'", nameof(dto));

        return new ProposedProfile(
            profileType, dto.Name, dto.DisplayName, dto.Description,
            dto.Model, dto.Provider, dto.SystemPrompt, dto.MaxTokens,
            dto.ReviewerFocus, dto.AdvisorMode, dto.AdvisorTrigger,
            dto.GroundingProviderType, dto.GroundingProviderSettings);
    }
}

public sealed record MaterializeTemplateProposalOutput(
    string CreatedTemplateName,
    IReadOnlyList<string> CreatedProfileNames,
    IReadOnlyList<string> Warnings);
