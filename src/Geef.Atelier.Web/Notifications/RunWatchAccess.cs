using System.Security.Claims;
using Geef.Atelier.Application.Runs;

namespace Geef.Atelier.Web.Notifications;

/// <summary>Who may watch which live updates of <see cref="IRunUpdateFeed"/>.</summary>
public static class RunWatchAccess
{
    /// <summary>True when the principal is signed in and carries the admin role.</summary>
    public static bool IsAtelierAdmin(this ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.IsInRole("admin");

    /// <summary>The signed-in user's name, or null for anonymous principals.</summary>
    public static string? WatcherName(this ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name) ? user.Identity.Name : null;

    /// <summary>True when <paramref name="user"/> may see run <paramref name="runId"/> (owner or administrator).</summary>
    public static async Task<bool> CanWatchRunAsync(this IRunService runService, ClaimsPrincipal? user, Guid runId, CancellationToken ct = default)
    {
        var name = user.WatcherName();
        if (name is null)
            return false;
        var run = await runService.GetRunAsync(runId, user.IsAtelierAdmin() ? null : name, ct);
        return run is not null;
    }

    /// <summary>The run-list topic for <paramref name="user"/>: all runs for administrators, own runs otherwise.</summary>
    public static string? RunListTopic(this ClaimsPrincipal? user) =>
        user.IsAtelierAdmin() ? RunUpdateTopics.AdminRuns
        : user.WatcherName() is { } name ? RunUpdateTopics.UserRuns(name)
        : null;
}
