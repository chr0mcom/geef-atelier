using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListRunsTool
{
    [McpServerTool, Description("Lists recent runs, optionally filtered by status. Non-admin users always see only their own runs.")]
    public static async Task<IReadOnlyList<RunSummaryDto>> ListRuns(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("Maximum number of runs to return (default 20).")] int limit = 20,
        [Description("Optional status filter (e.g. 'Pending', 'Running', 'Completed', 'Failed', 'Aborted').")] string? statusFilter = null,
        [Description("If true and caller is admin, returns runs from all users. Non-admin users: ignored.")] bool includeAllUsers = false,
        CancellationToken cancellationToken = default)
    {
        RunStatus? parsedStatus = statusFilter is not null && Enum.TryParse<RunStatus>(statusFilter, ignoreCase: true, out var s) ? s : null;
        var includeAll = currentUser.IsAdmin && includeAllUsers;
        var username = includeAll ? null : currentUser.Username;
        var runs = await runService.ListRunsAsync(limit, parsedStatus, username, cancellationToken);
        return runs.Select(r => new RunSummaryDto(
            r.Id.ToString(),
            r.Status.ToString(),
            r.CreatedAt,
            r.CreatedByUser)).ToList();
    }
}
