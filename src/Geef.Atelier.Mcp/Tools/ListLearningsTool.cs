using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Crew.Learning;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListLearningsTool
{
    public const string AdminOnlyMessage = "Only administrators can list learning entries, because they contain material from every user.";

    [McpServerTool, Description("Administrators only: lists extracted learning entries of all users from the continuous learning loop. Returns id, text preview, domain, status, source run, learning run, owner, and dates. Other users get an error.")]
    public static async Task<IReadOnlyList<LearningEntryDto>> ListLearnings(
        ILearningService learningService,
        ICurrentUserService currentUser,
        [Description("Optional status filter: 'Proposed', 'Approved', or 'Rejected'. Returns all statuses when omitted.")] string? status_filter = null,
        [Description("Optional domain filter (matches the crew template name of the source run, e.g. 'juristisch', 'akademisch').")] string? domain_filter = null,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdmin)
            throw new McpException(AdminOnlyMessage);

        LearningStatus? status = status_filter switch
        {
            "Proposed" => LearningStatus.Proposed,
            "Approved"  => LearningStatus.Approved,
            "Rejected"  => LearningStatus.Rejected,
            null        => null,
            _           => null
        };

        var entries = await learningService.ListAsync(status, domain_filter, cancellationToken);
        return entries
            .Select(e => new LearningEntryDto(
                e.Id,
                e.Text.Length > 300 ? e.Text[..300] + "…" : e.Text,
                e.SourceRunId,
                e.LearningRunId,
                e.Domain,
                e.Status.ToString(),
                e.OwnerUsername,
                e.CreatedAt,
                e.ApprovedAt))
            .ToList();
    }
}
